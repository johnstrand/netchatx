using Stanza.Core;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Xunit;

namespace Stanza.Core.Tests;

public class IqStanzaTests
{
    [Fact]
    public void CreateResult_SwapsToAndFrom_AndPreservesIdAndSetsResultType()
    {
        var fromJid = Jid.Parse("user@example.com/res");
        var toJid = Jid.Parse("server.example.com");
        var id = "iq-123";

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

    [Fact]
    public void CreateError_DefaultParameters_CreatesValidErrorStanza()
    {
        // Arrange
        var requestIq = new IqStanza(IqStanza.TypeGet, Jid.Parse("server.com"), Jid.Parse("user@example.com/res"), "iq123");

        // Act
        var errorIq = requestIq.CreateError("item-not-found");

        // Assert
        Assert.True(errorIq.IsError);
        Assert.Equal(IqStanza.TypeError, errorIq.Type);
        Assert.Equal("user@example.com/res", errorIq.To?.ToString());
        Assert.Equal("server.com", errorIq.From?.ToString());
        Assert.Equal("iq123", errorIq.Id);

        var errorElem = errorIq.RawElement.Element("error");
        Assert.NotNull(errorElem);
        Assert.Equal("cancel", errorElem.GetAttr("type"));

        var conditionElem = errorElem.Element("item-not-found");
        Assert.NotNull(conditionElem);
        Assert.Equal("urn:ietf:params:xml:ns:xmpp-stanzas", conditionElem.Namespace);

        var textElem = errorElem.Element("text");
        Assert.Null(textElem);
    }

    [Fact]
    public void CreateError_WithCustomTypeAndText_CreatesValidErrorStanzaWithText()
    {
        // Arrange
        var requestIq = new IqStanza(IqStanza.TypeSet, Jid.Parse("server.com"), Jid.Parse("user@example.com/res"), "iq456");

        // Act
        var errorIq = requestIq.CreateError("bad-request", "Invalid payload received", "modify");

        // Assert
        Assert.True(errorIq.IsError);
        Assert.Equal(IqStanza.TypeError, errorIq.Type);
        Assert.Equal("user@example.com/res", errorIq.To?.ToString());
        Assert.Equal("server.com", errorIq.From?.ToString());
        Assert.Equal("iq456", errorIq.Id);

        var errorElem = errorIq.RawElement.Element("error");
        Assert.NotNull(errorElem);
        Assert.Equal("modify", errorElem.GetAttr("type"));

        var conditionElem = errorElem.Element("bad-request");
        Assert.NotNull(conditionElem);
        Assert.Equal("urn:ietf:params:xml:ns:xmpp-stanzas", conditionElem.Namespace);

        var textElem = errorElem.Element("text");
        Assert.NotNull(textElem);
        Assert.Equal("urn:ietf:params:xml:ns:xmpp-stanzas", textElem.Namespace);
        Assert.Equal("Invalid payload received", textElem.Value);
    }

    [Fact]
    public void CreateError_WithEmptyText_DoesNotAddTextElement()
    {
        // Arrange
        var requestIq = new IqStanza(IqStanza.TypeGet, Jid.Parse("server.com"), Jid.Parse("user@example.com/res"), "iq789");

        // Act
        var errorIq = requestIq.CreateError("service-unavailable", "");

        // Assert
        var errorElem = errorIq.RawElement.Element("error");
        Assert.NotNull(errorElem);
        Assert.Null(errorElem.Element("text"));
    }
}
