using NetChatx.Core.Xml;
using Xunit;

namespace NetChatx.Core.Tests;

public class XmppStreamParserTests
{
    [Fact]
    public void ParseChunk_StreamHeaderAndFeatures_EmitsHeaderThenFeatures()
    {
        var parser = new XmppStreamParser();
        string input = "<?xml version='1.0'?><stream:stream from='example.com' id='12345' version='1.0' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><stream:features><starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/></stream:features>";

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
}
