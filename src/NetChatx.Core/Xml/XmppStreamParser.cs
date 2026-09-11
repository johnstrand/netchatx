using System.IO.Pipelines;
using System.Text;

namespace NetChatx.Core.Xml;

/// <summary>
/// Asynchronously streams and frames XMPP stanzas and stream headers from a PipeReader.
/// Designed for low latency, memory safety, and non-blocking asynchronous processing with internal queuing.
/// </summary>
public sealed class XmppStreamParser
{
    private enum ParserState
    {
        AwaitingStreamHeader,
        InsideStream
    }

    private ParserState _state = ParserState.AwaitingStreamHeader;
    private readonly StringBuilder _buffer = new(4096);
    private readonly Queue<XmppElement> _readyElements = new();
    private int _depth;
    private bool _inTag;
    private bool _inQuotes;
    private char _quoteChar;
    private bool _inCData;
    private bool _inComment;

    public void Reset()
    {
        _state = ParserState.AwaitingStreamHeader;
        _buffer.Clear();
        _readyElements.Clear();
        _depth = 0;
        _inTag = false;
        _inQuotes = false;
        _inCData = false;
        _inComment = false;
    }

    /// <summary>
    /// Reads the next complete XmppElement from the reader, using queued elements if already parsed.
    /// </summary>
    public async ValueTask<XmppElement> ReadElementAsync(PipeReader reader, CancellationToken cancellationToken = default)
    {
        while (_readyElements.Count == 0)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
            {
                throw new EndOfStreamException("End of stream reached while waiting for XMPP stanza.");
            }

            foreach (var segment in buffer)
            {
                string text = Encoding.UTF8.GetString(segment.Span);
                foreach (char c in text)
                {
                    var elem = ProcessChar(c);
                    if (elem is not null)
                    {
                        _readyElements.Enqueue(elem);
                    }
                }
            }

            reader.AdvanceTo(buffer.End);

            if (_readyElements.Count > 0)
            {
                break;
            }

            if (result.IsCompleted)
            {
                throw new EndOfStreamException("Stream completed before complete stanza was received.");
            }
        }

        return _readyElements.Dequeue();
    }

    /// <summary>
    /// Asynchronously streams elements continuously until cancellation or stream end.
    /// </summary>
    public async IAsyncEnumerable<XmppElement> ReadAllAsync(
        PipeReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            XmppElement elem;
            try
            {
                elem = await ReadElementAsync(reader, cancellationToken);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            yield return elem;
        }
    }

    /// <summary>
    /// Feeds a chunk of text directly (for unit testing) and returns all newly completed elements.
    /// </summary>
    public IEnumerable<XmppElement> ParseChunk(string chunk)
    {
        foreach (char c in chunk)
        {
            var elem = ProcessChar(c);
            if (elem is not null)
            {
                yield return elem;
            }
        }
    }

    private XmppElement? ProcessChar(char c)
    {
        _buffer.Append(c);

        if (_state == ParserState.AwaitingStreamHeader)
        {
            string current = _buffer.ToString();
            int streamIndex = current.IndexOf("<stream:stream", StringComparison.OrdinalIgnoreCase);
            if (streamIndex < 0)
            {
                streamIndex = current.IndexOf("<stream ", StringComparison.OrdinalIgnoreCase);
            }

            if (streamIndex >= 0)
            {
                int tagEnd = current.IndexOf('>', streamIndex);
                if (tagEnd > streamIndex)
                {
                    string tagContent = current.Substring(streamIndex, tagEnd - streamIndex + 1);
                    if (CountQuotes(tagContent) % 2 == 0)
                    {
                        string parsedTag = tagContent.TrimEnd('>');
                        if (!parsedTag.EndsWith('/'))
                        {
                            parsedTag += "/>";
                        }
                        else
                        {
                            parsedTag += ">";
                        }

                        _buffer.Clear();
                        _state = ParserState.InsideStream;
                        _depth = 0;
                        _inTag = false;
                        _inQuotes = false;

                        return XmppElement.Parse(parsedTag);
                    }
                }
            }

            if (_buffer.Length > 8192 && streamIndex < 0)
            {
                _buffer.Remove(0, 4096);
            }

            return null;
        }

        int len = _buffer.Length;

        // Check for CDATA
        if (!_inCData && len >= 9 && _buffer.ToString(len - 9, 9) == "<![CDATA[")
        {
            _inCData = true;
            return null;
        }
        if (_inCData)
        {
            if (len >= 3 && _buffer.ToString(len - 3, 3) == "]]>")
            {
                _inCData = false;
                _inTag = false;
            }
            return null;
        }

        // Check for Comment
        if (!_inComment && len >= 4 && _buffer.ToString(len - 4, 4) == "<!--")
        {
            _inComment = true;
            return null;
        }
        if (_inComment)
        {
            if (len >= 3 && _buffer.ToString(len - 3, 3) == "-->")
            {
                _inComment = false;
                _inTag = false;
            }
            return null;
        }

        // Check for Quotes ONLY within an XML tag
        if (_inTag)
        {
            if (_inQuotes)
            {
                if (c == _quoteChar)
                {
                    _inQuotes = false;
                }
                return null;
            }
            if (c is '"' or '\'')
            {
                _inQuotes = true;
                _quoteChar = c;
                return null;
            }
        }

        // Detect closing of stream: </stream:stream>
        string bufStr = _buffer.ToString().Trim();
        if (bufStr.Equals("</stream:stream>", StringComparison.OrdinalIgnoreCase) ||
            bufStr.Equals("</stream>", StringComparison.OrdinalIgnoreCase))
        {
            _buffer.Clear();
            _inTag = false;
            _inQuotes = false;
            var closeStream = new XmppElement("stream:stream")
                .Attr("closed", "true");
            return closeStream;
        }

        // Tag tracking
        if (c == '<')
        {
            _inTag = true;
            if (_depth == 0)
            {
                _buffer.Clear();
                _buffer.Append('<');
            }
        }
        else if (c == '>')
        {
            _inTag = false;
            string s = _buffer.ToString();
            int openAngle = s.LastIndexOf('<');
            if (openAngle >= 0)
            {
                string tag = s.Substring(openAngle);
                if (tag.StartsWith("</", StringComparison.Ordinal))
                {
                    _depth--;
                }
                else if (tag.EndsWith("/>", StringComparison.Ordinal))
                {
                    if (_depth == 0)
                    {
                        string stanzaXml = _buffer.ToString().Trim();
                        _buffer.Clear();
                        if (!string.IsNullOrEmpty(stanzaXml))
                        {
                            return XmppElement.Parse(stanzaXml);
                        }
                    }
                }
                else if (!tag.StartsWith("<?", StringComparison.Ordinal) && !tag.StartsWith("<!", StringComparison.Ordinal))
                {
                    _depth++;
                }
            }

            if (_depth <= 0)
            {
                string stanzaXml = _buffer.ToString().Trim();
                _buffer.Clear();
                _depth = 0;
                if (!string.IsNullOrEmpty(stanzaXml) && stanzaXml.StartsWith('<'))
                {
                    return XmppElement.Parse(stanzaXml);
                }
            }
        }

        return null;
    }

    private static int CountQuotes(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '"' or '\'') count++;
        }
        return count;
    }
}
