using System.IO.Pipelines;
using System.Text;
using Stanza.Core.Xml;
using Xunit;

namespace Stanza.Core.Tests;

public class XmppStreamParserTests
{
    [Fact]
    public void ParseChunk_StreamHeaderAndFeatures_EmitsHeaderThenFeatures()
    {
        var parser = new XmppStreamParser();
        var input = "<?xml version='1.0'?><stream:stream from='example.com' id='12345' version='1.0' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><stream:features><starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/></stream:features>";

        var elements = parser.ParseChunk(input).ToList();

        Assert.Equal(2, elements.Count);
        Assert.Equal("stream:stream", elements[0].FullName);
        Assert.Equal("example.com", elements[0].GetAttr("from"));
        Assert.Equal("12345", elements[0].GetAttr("id"));

        Assert.Equal("stream:features", elements[1].FullName);
        Assert.NotNull(elements[1].Element("starttls"));
    }

    [Fact]
    public void ParseChunk_MultipleStanzasInChunks_ParsesAcrossChunkBoundaries()
    {
        var parser = new XmppStreamParser();
        // Send stream header first
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        // Send a message split across 3 tiny chunks
        var chunk1 = "<message type='chat' ";
        var chunk2 = "to='alice@example.com'><body>Hello ";
        var chunk3 = "world!</body></message>";

        var res1 = parser.ParseChunk(chunk1).ToList();
        var res2 = parser.ParseChunk(chunk2).ToList();
        var res3 = parser.ParseChunk(chunk3).ToList();

        Assert.Empty(res1);
        Assert.Empty(res2);
        Assert.Single(res3);

        var msg = res3[0];
        Assert.Equal("message", msg.Name);
        Assert.Equal("chat", msg.GetAttr("type"));
        Assert.Equal("alice@example.com", msg.GetAttr("to"));
        Assert.Equal("Hello world!", msg.Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_SelfClosingStanzas_HandledCorrectly()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var res = parser.ParseChunk("<r xmlns='urn:xmpp:sm:3'/>").ToList();

        Assert.Single(res);
        Assert.Equal("r", res[0].Name);
        Assert.Equal("urn:xmpp:sm:3", res[0].GetAttr("xmlns"));
    }

    [Fact]
    public void ParseChunk_TextWithApostrophesAndQuotes_ParsesSuccessfully()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var xml = "<message type='chat' to='richard@squishythoughts.com'><body>/me retracted a previous message, but it's unsupported by your client. \"Hello!\"</body></message>";
        var res = parser.ParseChunk(xml).ToList();

        Assert.Single(res);
        Assert.Equal("message", res[0].Name);
        Assert.Equal("/me retracted a previous message, but it's unsupported by your client. \"Hello!\"", res[0].Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_CDataContainingAngledBrackets_PreservesContent()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var xml = "<message type='chat' to='alice@example.com'><body><![CDATA[ <not a tag> ]]></body></message>";
        var elements = parser.ParseChunk(xml).ToList();

        Assert.Single(elements);
        var msg = elements[0];
        Assert.Equal("message", msg.Name);
        Assert.Equal(" <not a tag> ", msg.Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_CDataWithMultipleAngledBracketsSplitAcrossChunks_ParsesCorrectly()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var chunk1 = "<message to='alice@example.com'><body><![CDATA[ if (x > 5 && ";
        var chunk2 = "y < 10) { return a > b; } ]]></body></message>";

        var res1 = parser.ParseChunk(chunk1).ToList();
        var res2 = parser.ParseChunk(chunk2).ToList();

        Assert.Empty(res1);
        Assert.Single(res2);
        Assert.Equal(" if (x > 5 && y < 10) { return a > b; } ", res2[0].Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_XmlCommentsWithDashesAndAngledBrackets_ParsedAndIgnored()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var xml = "<message to='alice@example.com'><!-- comment with <tags> & dashes --><body>hello</body></message>";
        var elements = parser.ParseChunk(xml).ToList();

        Assert.Single(elements);
        var msg = elements[0];
        Assert.Equal("message", msg.Name);
        Assert.Equal("hello", msg.Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_XmlCommentsBetweenChildElements_DoesNotCorruptHierarchy()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var xml = "<iq type='result' id='roster1'><query xmlns='jabber:iq:roster'><!-- item with - dashes - and <angles> --><item jid='contact@example.com'/></query></iq>";
        var elements = parser.ParseChunk(xml).ToList();

        Assert.Single(elements);
        var iq = elements[0];
        Assert.Equal("iq", iq.Name);
        var query = iq.Element("query", "jabber:iq:roster");
        Assert.NotNull(query);
        var item = query.Element("item");
        Assert.NotNull(item);
        Assert.Equal("contact@example.com", item.GetAttr("jid"));
    }

    [Fact]
    public void ParseChunk_DeeplyNestedXmlElements_ParsesFullHierarchy()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var xml = "<a><b><c><d><e>deep</e></d></c></b></a>";
        var elements = parser.ParseChunk(xml).ToList();

        Assert.Single(elements);
        var elem = elements[0];
        Assert.Equal("a", elem.Name);
        var deep = elem.Element("b")?.Element("c")?.Element("d")?.Element("e");
        Assert.NotNull(deep);
        Assert.Equal("deep", deep.Value);
    }

    [Fact]
    public void ParseChunk_VeryDeeplyNestedXmlElements_ParsesSuccessfully()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        const int depth = 20;
        var sbOpen = new StringBuilder("<root>");
        var sbClose = new StringBuilder("</root>");
        for (var i = 1; i <= depth; i++)
        {
            sbOpen.Append($"<level{i}>");
            sbClose.Insert(0, $"</level{i}>");
        }
        var xml = $"{sbOpen}deep-payload{sbClose}";

        var elements = parser.ParseChunk(xml).ToList();
        Assert.Single(elements);

        var current = elements[0];
        for (var i = 1; i <= depth; i++)
        {
            current = current.Element($"level{i}");
            Assert.NotNull(current);
        }
        Assert.Equal("deep-payload", current.Value);
    }

    [Fact]
    public void ParseChunk_SelfClosingElementNestedWithinStandardElement_ParsesChildren()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var xml = "<message><body>hi</body><active xmlns='http://jabber.org/protocol/chatstates'/></message>";
        var elements = parser.ParseChunk(xml).ToList();

        Assert.Single(elements);
        var msg = elements[0];
        Assert.Equal("message", msg.Name);
        Assert.Equal("hi", msg.Element("body")?.Value);

        var active = msg.Element("active", "http://jabber.org/protocol/chatstates");
        Assert.NotNull(active);
        Assert.Equal("active", active.Name);
    }

    [Fact]
    public void ParseChunk_MultipleSelfClosingElementsNestedWithinStandardElement_ParsesAllChildren()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var xml = "<message to='alice@example.com'><body>test</body><delay xmlns='urn:xmpp:delay' stamp='2026-09-26T00:00:00Z'/><active xmlns='http://jabber.org/protocol/chatstates'/><request xmlns='urn:xmpp:receipts'/></message>";
        var elements = parser.ParseChunk(xml).ToList();

        Assert.Single(elements);
        var msg = elements[0];
        Assert.Equal("test", msg.Element("body")?.Value);
        Assert.NotNull(msg.Element("delay", "urn:xmpp:delay"));
        Assert.Equal("2026-09-26T00:00:00Z", msg.Element("delay")?.GetAttr("stamp"));
        Assert.NotNull(msg.Element("active", "http://jabber.org/protocol/chatstates"));
        Assert.NotNull(msg.Element("request", "urn:xmpp:receipts"));
    }

    [Fact]
    public void ParseChunk_ClosingStreamStreamStanza_ReturnsClosedElement()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var elements = parser.ParseChunk("</stream:stream>").ToList();

        Assert.Single(elements);
        var elem = elements[0];
        Assert.Equal("stream:stream", elem.Name);
        Assert.Equal("true", elem.GetAttr("closed"));
    }

    [Fact]
    public void ParseChunk_ClosingStreamShortStanza_ReturnsClosedElement()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var elements = parser.ParseChunk("</stream>").ToList();

        Assert.Single(elements);
        var elem = elements[0];
        Assert.Equal("stream:stream", elem.Name);
        Assert.Equal("true", elem.GetAttr("closed"));
    }

    [Fact]
    public async Task ReadElementAsync_SplitMultiByteUtf8AcrossPipeChunks_DecodesCorrectly()
    {
        var parser = new XmppStreamParser();
        var pipe = new Pipe();

        // 1. Send stream header
        var headerBytes = Encoding.UTF8.GetBytes("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>");
        await pipe.Writer.WriteAsync(headerBytes);
        await pipe.Writer.FlushAsync();

        var header = await parser.ReadElementAsync(pipe.Reader);
        Assert.Equal("stream:stream", header.FullName);

        // 2. Prepare message containing multi-byte characters:
        // Swedish: åäö (2 bytes each), Japanese: 日本語 (3 bytes each), Emojis: 🚀 🌍 (4 bytes each)
        var msgXml = "<message to='bob@example.com'><body>Hello 🚀 Swedish: åäö 日本語 🌍</body></message>";
        var msgBytes = Encoding.UTF8.GetBytes(msgXml);

        // Split the byte array across chunks right through multi-byte character byte sequences
        const int chunkSize = 3;
        for (var offset = 0; offset < msgBytes.Length; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, msgBytes.Length - offset);
            await pipe.Writer.WriteAsync(msgBytes.AsMemory(offset, count));
            await pipe.Writer.FlushAsync();
        }

        var stanza = await parser.ReadElementAsync(pipe.Reader);
        Assert.Equal("message", stanza.Name);
        Assert.Equal("Hello 🚀 Swedish: åäö 日本語 🌍", stanza.Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_SplitMultiByteUtf8Bytes_DecodesCorrectly()
    {
        var parser = new XmppStreamParser();
        var headerBytes = Encoding.UTF8.GetBytes("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>");
        _ = parser.ParseChunk(headerBytes).ToList();

        var xml = "<message to='bob@example.com'><body>Emoji 🚀 and non-ASCII åäö</body></message>";
        var bytes = Encoding.UTF8.GetBytes(xml);

        // Split into 2-byte chunks across boundaries
        var elements = new List<XmppElement>();
        for (var i = 0; i < bytes.Length; i += 2)
        {
            var len = Math.Min(2, bytes.Length - i);
            var chunk = new byte[len];
            Array.Copy(bytes, i, chunk, 0, len);
            elements.AddRange(parser.ParseChunk(chunk));
        }

        Assert.Single(elements);
        Assert.Equal("message", elements[0].Name);
        Assert.Equal("Emoji 🚀 and non-ASCII åäö", elements[0].Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_SplitSurrogatePairChars_HandledCorrectly()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        // 🚀 is represented by surrogate pair \uD83D\uDE80
        var chunk1 = "<message><body>Hello \uD83D";
        var chunk2 = "\uDE80 world!</body></message>";

        var res1 = parser.ParseChunk(chunk1).ToList();
        var res2 = parser.ParseChunk(chunk2).ToList();

        Assert.Empty(res1);
        Assert.Single(res2);
        Assert.Equal("Hello 🚀 world!", res2[0].Element("body")?.Value);
    }

    [Fact]
    public void ParseChunk_BufferExceedingMaxElementSize_InsideStream_ThrowsInvalidOperationException()
    {
        var parser = new XmppStreamParser(maxElementSize: 100);
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var oversizedPayload = "<message><body>" + new string('a', 150) + "</body></message>";

        var ex = Assert.Throws<InvalidOperationException>(() => parser.ParseChunk(oversizedPayload).ToList());
        Assert.Contains("maximum allowed limit", ex.Message);
    }

    [Fact]
    public void ParseChunk_BufferExceedingDefaultMaxElementSize_InsideStream_ThrowsInvalidOperationException()
    {
        var parser = new XmppStreamParser();
        _ = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();

        var oversizedPayload = "<message><body>" + new string('x', parser.MaxElementSize + 10);

        Assert.Throws<InvalidOperationException>(() => parser.ParseChunk(oversizedPayload).ToList());
    }

    [Fact]
    public void ParseChunk_BufferExceedingMaxElementSize_AwaitingStreamHeader_DoesNotThrow()
    {
        var parser = new XmppStreamParser(maxElementSize: 100);

        // Feeding large noise before stream header does NOT throw inside AwaitingStreamHeader
        var leadingNoise = new string(' ', 500);
        var noiseResult = parser.ParseChunk(leadingNoise).ToList();
        Assert.Empty(noiseResult);

        // Header still parses successfully
        var headerResult = parser.ParseChunk("<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>").ToList();
        Assert.Single(headerResult);
        Assert.Equal("stream:stream", headerResult[0].FullName);
    }
}
