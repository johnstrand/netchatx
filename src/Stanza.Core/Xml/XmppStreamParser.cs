using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace Stanza.Core.Xml;

/// <summary>
/// Asynchronously streams and frames XMPP stanzas and stream headers from a PipeReader.
/// Designed for low latency, memory safety, and non-blocking asynchronous processing with internal queuing.
/// </summary>
public sealed class XmppStreamParser
{
    public const int DefaultMaxElementSize = 1024 * 1024; // 1 MB

    private enum ParserState
    {
        AwaitingStreamHeader,
        InsideStream
    }

    private ParserState _state = ParserState.AwaitingStreamHeader;
    private readonly StringBuilder _buffer = new(4096);
    private readonly Queue<XmppElement> _readyElements = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private int _maxElementSize = DefaultMaxElementSize;
    private int _depth;
    private bool _inTag;
    private bool _inQuotes;
    private char _quoteChar;
    private bool _inCData;
    private bool _inComment;

    public int MaxElementSize
    {
        get => _maxElementSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxElementSize = value;
        }
    }

    public XmppStreamParser(int maxElementSize = DefaultMaxElementSize)
    {
        MaxElementSize = maxElementSize;
    }

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
        _decoder.Reset();
    }

    /// <summary>
    /// Reads the next complete XmppElement from the reader, using queued elements if already parsed.
    /// </summary>
    public async ValueTask<XmppElement> ReadElementAsync(PipeReader reader, CancellationToken cancellationToken = default)
    {
        while (_readyElements.Count == 0)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
            {
                throw new EndOfStreamException("End of stream reached while waiting for XMPP stanza.");
            }

            foreach (var segment in buffer)
            {
                var charBuffer = ArrayPool<char>.Shared.Rent(segment.Length + 4);
                try
                {
                    var charCount = _decoder.GetChars(segment.Span, charBuffer.AsSpan(), flush: false);
                    for (var i = 0; i < charCount; i++)
                    {
                        var elem = ProcessChar(charBuffer[i]);
                        if (elem is not null)
                        {
                            _readyElements.Enqueue(elem);
                        }
                    }
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(charBuffer);
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
                elem = await ReadElementAsync(reader, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            yield return elem;
        }
    }

    /// <summary>
    /// Feeds a chunk of UTF-8 encoded bytes directly (for unit testing) and returns all newly completed elements.
    /// </summary>
    public IEnumerable<XmppElement> ParseChunk(byte[] chunk)
    {
        var charBuffer = new char[chunk.Length + 4];
        var charCount = _decoder.GetChars(chunk, 0, chunk.Length, charBuffer, 0, false);
        for (var i = 0; i < charCount; i++)
        {
            var elem = ProcessChar(charBuffer[i]);
            if (elem is not null)
            {
                yield return elem;
            }
        }
    }

    /// <summary>
    /// Feeds a chunk of text directly (for unit testing) and returns all newly completed elements.
    /// </summary>
    public IEnumerable<XmppElement> ParseChunk(string chunk)
    {
        foreach (var c in chunk)
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
            var current = _buffer.ToString();
            var streamIndex = current.IndexOf("<stream:stream", StringComparison.OrdinalIgnoreCase);
            if (streamIndex < 0)
            {
                streamIndex = current.IndexOf("<stream ", StringComparison.OrdinalIgnoreCase);
            }

            if (streamIndex >= 0)
            {
                var tagEnd = current.IndexOf('>', streamIndex);
                if (tagEnd > streamIndex)
                {
                    var tagContent = current.Substring(streamIndex, tagEnd - streamIndex + 1);
                    if (CountQuotes(tagContent) % 2 == 0)
                    {
                        var parsedTag = tagContent.TrimEnd('>');
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

        if (_buffer.Length > _maxElementSize)
        {
            Reset();
            throw new InvalidOperationException($"Element size exceeded maximum allowed limit of {_maxElementSize} characters.");
        }

        var len = _buffer.Length;

        // Check for CDATA
        if (!_inCData && len >= 9 && EndsWith(_buffer, "<![CDATA["))
        {
            _inCData = true;
            return null;
        }
        if (_inCData)
        {
            if (len >= 3 && EndsWith(_buffer, "]]>"))
            {
                _inCData = false;
                _inTag = false;
            }
            return null;
        }

        // Check for Comment
        if (!_inComment && len >= 4 && EndsWith(_buffer, "<!--"))
        {
            _inComment = true;
            return null;
        }
        if (_inComment)
        {
            if (len >= 3 && EndsWith(_buffer, "-->"))
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
        if (c == '>' && _depth <= 1 && len <= 30)
        {
            var bufStr = _buffer.ToString().Trim();
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
            var openAngle = -1;
            for (var i = _buffer.Length - 1; i >= 0; i--)
            {
                if (_buffer[i] == '<')
                {
                    openAngle = i;
                    break;
                }
            }

            if (openAngle >= 0)
            {
                var tagLen = _buffer.Length - openAngle;
                if (tagLen >= 2 && _buffer[openAngle + 1] == '/')
                {
                    _depth--;
                }
                else if (tagLen >= 2 && _buffer[_buffer.Length - 2] == '/')
                {
                    if (_depth == 0)
                    {
                        var stanzaXml = _buffer.ToString().Trim();
                        _buffer.Clear();
                        if (!string.IsNullOrEmpty(stanzaXml))
                        {
                            return XmppElement.Parse(stanzaXml);
                        }
                    }
                }
                else if (tagLen >= 2 && _buffer[openAngle + 1] != '?' && _buffer[openAngle + 1] != '!')
                {
                    _depth++;
                }
            }

            if (_depth <= 0)
            {
                var stanzaXml = _buffer.ToString().Trim();
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

    private static bool EndsWith(StringBuilder sb, string suffix)
    {
        if (sb.Length < suffix.Length) return false;
        var start = sb.Length - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (sb[start + i] != suffix[i]) return false;
        }
        return true;
    }

    private static int CountQuotes(string text)
    {
        var count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '"' or '\'') count++;
        }
        return count;
    }
}
