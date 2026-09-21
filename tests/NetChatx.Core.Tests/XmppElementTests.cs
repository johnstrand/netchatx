using NetChatx.Core.Xml;
using Xunit;

namespace NetChatx.Core.Tests;

public class XmppElementTests
{
    [Fact]
    public void ParseAndEmit_SimpleStanza_PreservesStructure()
    {
        var xml = "<message to='alice@example.com' from='bob@example.com' type='chat'><body xmlns='jabber:client'>Hello World!</body></message>";
        var elem = XmppElement.Parse(xml);

        Assert.Equal("message", elem.Name);
        Assert.Equal("alice@example.com", elem.GetAttr("to"));
        Assert.Equal("bob@example.com", elem.GetAttr("from"));
        Assert.Equal("chat", elem.GetAttr("type"));

        var body = elem.Element("body");
        Assert.NotNull(body);
        Assert.Equal("Hello World!", body.Value);
    }

    [Fact]
    public void FluentBuilder_BuildsCorrectXml()
    {
        var iq = new XmppElement("iq")
            .Attr("id", "req-1")
            .Attr("type", "get")
            .Child(new XmppElement("query", "jabber:iq:roster"));

        var xml = iq.ToXmlString();
        Assert.Contains("id=\"req-1\"", xml);
        Assert.Contains("type=\"get\"", xml);
        Assert.Contains("xmlns=\"jabber:iq:roster\"", xml);
    }

    [Fact]
    public void ParseAndEmit_WithXmlLangAttribute_SerializesCorrectlyWithoutException()
    {
        var xml = "<message to='alice@example.com' from='bob@example.com' type='chat' xml:lang='en'><body>Hello with lang!</body></message>";
        var elem = XmppElement.Parse(xml);

        Assert.Equal("en", elem.GetAttr("xml:lang"));
        var outputXml = elem.ToXmlString();
        Assert.Contains("xml:lang=\"en\"", outputXml);
    }

    [Fact]
    public void WriteTo_WithPrefixAndNamespace_SerializesCorrectly()
    {
        var elem = new XmppElement("stream", "http://etherx.jabber.org/streams", "stream")
            .Attr("version", "1.0");

        var xml = elem.ToXmlString();
        Assert.StartsWith("<stream:stream", xml);
        Assert.Contains("xmlns:stream=\"http://etherx.jabber.org/streams\"", xml);
        Assert.Contains("version=\"1.0\"", xml);
    }

    [Fact]
    public void WriteTo_WithCustomXmlnsPrefixAttribute_SerializesCorrectly()
    {
        var elem = new XmppElement("message")
            .Attr("xmlns:custom", "urn:custom:ns")
            .Attr("custom:attr", "val");

        var xml = elem.ToXmlString();
        Assert.Contains("xmlns:custom=\"urn:custom:ns\"", xml);
        Assert.Contains("custom:attr=\"val\"", xml);
    }

    [Fact]
    public void WriteTo_WithNestedChildrenAndValues_SerializesHierarchy()
    {
        var parent = new XmppElement("presence")
            .Attr("type", "subscribe")
            .Child(new XmppElement("status").Text("Online"))
            .Child(new XmppElement("priority").Text("10"));

        var xml = parent.ToXmlString();
        Assert.Contains("<status>Online</status>", xml);
        Assert.Contains("<priority>10</priority>", xml);
    }
}
