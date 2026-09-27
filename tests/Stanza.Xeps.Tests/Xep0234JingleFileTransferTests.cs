using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Sharing;
using Xunit;

namespace Stanza.Xeps.Tests;

public sealed class Xep0234JingleFileTransferTests
{
    [Fact]
    public async Task OnIncomingElementAsync_SessionInitiate_RaisesOfferReceived()
    {
        var feature = new Xep0234JingleFileTransfer();
        JingleFileTransferSessionEventArgs? captured = null;
        feature.OfferReceived += args => captured = args;

        var iq = XmppElement.Parse(
            "<iq type='set' from='alice@example.com/resource'><jingle xmlns='urn:xmpp:jingle:1' action='session-initiate' sid='sid-123'><description xmlns='urn:xmpp:jingle:apps:file-transfer:5'><file><name>photo.png</name><size>12345</size><media-type>image/png</media-type></file></description></jingle></iq>");

        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse("bob@example.com"), Password = "pw" }, new LoopbackTransport());
        await feature.OnIncomingElementAsync(client, iq);

        Assert.NotNull(captured);
        Assert.Equal("sid-123", captured!.SessionId);
        Assert.Equal("photo.png", captured.Name);
        Assert.Equal(12345, captured.Size);
        Assert.Equal("image/png", captured.MediaType);
    }

    [Fact]
    public async Task OnIncomingElementAsync_SessionAcceptAndTerminate_RaisesEvents()
    {
        var feature = new Xep0234JingleFileTransfer();
        var accepted = false;
        var rejected = false;
        feature.OfferAccepted += _ => accepted = true;
        feature.OfferRejected += _ => rejected = true;

        var acceptIq = XmppElement.Parse("<iq type='set' from='alice@example.com/resource'><jingle xmlns='urn:xmpp:jingle:1' action='session-accept' sid='sid-1'/></iq>");
        var rejectIq = XmppElement.Parse("<iq type='set' from='alice@example.com/resource'><jingle xmlns='urn:xmpp:jingle:1' action='session-terminate' sid='sid-1'/></iq>");

        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse("bob@example.com"), Password = "pw" }, new LoopbackTransport());
        await feature.OnIncomingElementAsync(client, acceptIq);
        await feature.OnIncomingElementAsync(client, rejectIq);

        Assert.True(accepted);
        Assert.True(rejected);
    }
}
