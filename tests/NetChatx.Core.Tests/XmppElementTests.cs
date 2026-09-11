using NetChatx.Core.Xml;
using Xunit;

namespace NetChatx.Core.Tests;

public class XmppElementTests
{
    [Fact]
    public void ParseAndEmit_SimpleStanza_PreservesStructure()
    {
        string xml = "<message to='alice@example.com' from='bob@example.com' type='chat'><body xmlns='jabber:client'>Hello World!</body></message>";
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

        string xml = iq.ToXmlString();
        Assert.Contains("id=\"req-1\"", xml);
        Assert.Contains("type=\"get\"", xml);
        Assert.Contains("xmlns=\"jabber:iq:roster\"", xml);
    }

    [Fact]
    public void ParseAndEmit_WithXmlLangAttribute_SerializesCorrectlyWithoutException()
    {
        string xml = "<message to='alice@example.com' from='bob@example.com' type='chat' xml:lang='en'><body>Hello with lang!</body></message>";
        var elem = XmppElement.Parse(xml);

        Assert.Equal("en", elem.GetAttr("xml:lang"));
        string outputXml = elem.ToXmlString();
        Assert.Contains("xml:lang=\"en\"", outputXml);
    }
}
