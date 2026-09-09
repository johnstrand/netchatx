using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using Xunit;

namespace NetChatx.Core.Tests;

public class IqStanzaTests
{
    [Fact]
    public void CreateResult_SwapsToAndFrom_AndPreservesIdAndSetsResultType()
    {
        var fromJid = Jid.Parse("user@example.com/res");
        var toJid = Jid.Parse("server.example.com");
        string id = "iq-123";

        var request = new IqStanza(IqStanza.TypeGet, to: toJid, from: fromJid, id: id);
        var result = request.CreateResult();

        Assert.Equal(IqStanza.TypeResult, result.Type);
        Assert.True(result.IsResult);
        Assert.False(result.IsGet);
        Assert.False(result.IsSet);
        Assert.False(result.IsError);
        Assert.Equal(fromJid, result.To);
        Assert.Equal(toJid, result.From);
        Assert.Equal(id, result.Id);
    }

    [Fact]
    public void CreateResult_WithPayload_AttachesPayloadToRawElement()
    {
        var request = IqStanza.CreateGet(Jid.Parse("user@example.com"), "iq-456");
        var payload = new XmppElement("query", "jabber:iq:roster");

        var result = request.CreateResult(payload);

        Assert.Single(result.RawElement.Children);
        var queryElem = result.RawElement.Element("query");
        Assert.NotNull(queryElem);
        Assert.Equal("jabber:iq:roster", queryElem.Namespace);
    }

    [Fact]
    public void CreateResult_WithNullPayload_HasNoChildren()
    {
        var request = IqStanza.CreateSet(Jid.Parse("user@example.com"), "iq-789");

        var result = request.CreateResult(null);

        Assert.Empty(result.RawElement.Children);
    }

    [Fact]
    public void CreateResult_WhenFromAndToAreNull_ResultHasNullToAndFrom()
    {
        var request = new IqStanza(IqStanza.TypeGet, to: null, from: null, id: "iq-999");

        var result = request.CreateResult();

        Assert.Null(result.To);
        Assert.Null(result.From);
        Assert.Equal("iq-999", result.Id);
        Assert.True(result.IsResult);
    }
}
