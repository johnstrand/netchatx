using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Resilience;
using Xunit;

namespace Stanza.Xeps.Tests;

public class Xep0198ResumptionTests
{
    [Fact]
    public async Task OutgoingStanzas_WhenEnabled_AreQueuedForRetransmission()
    {
        var sm = new Xep0198StreamManagement();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await sm.AttachAsync(client);

        // Not enabled yet: outgoing stanzas are not queued
        var msg0 = new XmppElement("message").Attr("to", "peer@example.com");
        await sm.OnOutgoingElementAsync(client, msg0);
        Assert.Equal(0, sm.UnacknowledgedCount);
        Assert.Equal(0u, sm.OutboundHandled);

        // Simulate enabled element
        var enabledElem = new XmppElement("enabled", Xep0198StreamManagement.NsSm)
            .Attr("id", "token-1");
        await sm.OnIncomingElementAsync(client, enabledElem);
        Assert.True(sm.IsEnabled);
        Assert.Equal("token-1", sm.ResumeId);

        // Now outgoing stanzas are queued
        var msg1 = new XmppElement("message").Attr("to", "peer1@example.com");
        var msg2 = new XmppElement("message").Attr("to", "peer2@example.com");
        await sm.OnOutgoingElementAsync(client, msg1);
        await sm.OnOutgoingElementAsync(client, msg2);

        Assert.Equal(2, sm.UnacknowledgedCount);
        Assert.Equal(2u, sm.OutboundHandled);

        // Server acknowledges first stanza (h = 1)
        var ack1 = new XmppElement("a", Xep0198StreamManagement.NsSm).Attr("h", "1");
        await sm.OnIncomingElementAsync(client, ack1);

        Assert.Equal(1, sm.UnacknowledgedCount);
        Assert.Equal(1u, sm.LastAckedByServer);
        Assert.Equal("peer2@example.com", sm.UnacknowledgedStanzas[0].GetAttr("to"));

        // Server acknowledges second stanza (h = 2)
        var ack2 = new XmppElement("a", Xep0198StreamManagement.NsSm).Attr("h", "2");
        await sm.OnIncomingElementAsync(client, ack2);

        Assert.Equal(0, sm.UnacknowledgedCount);
        Assert.Equal(2u, sm.LastAckedByServer);
    }

    [Fact]
    public void HandleAck_SupportsUintModulo2Pow32Wraparound()
    {
        var sm = new Xep0198StreamManagement();

        // Simulate state near uint wrap: last ack was uint.MaxValue
        sm.LastAckedByServer = uint.MaxValue;

        uint? receivedAck = null;
        sm.AckReceived += h => receivedAck = h;

        // Server acks 1 (wrapped past uint.MaxValue)
        sm.HandleAck(1);
        Assert.Equal(1u, sm.LastAckedByServer);
        Assert.Equal(1u, receivedAck);

        // Out-of-order/older ack (e.g. 0) should be ignored
        sm.HandleAck(0);
        Assert.Equal(1u, sm.LastAckedByServer);
    }

    [Fact]
    public async Task TryResumeAsync_WhenResumed_RetransmitsOnlyUnacknowledgedStanzas()
    {
        var sm = new Xep0198StreamManagement();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await sm.AttachAsync(client);

        // Enable SM with resume
        await sm.OnIncomingElementAsync(client, new XmppElement("enabled", Xep0198StreamManagement.NsSm).Attr("id", "token-abc"));

        // Send 3 messages
        await sm.OnOutgoingElementAsync(client, new XmppElement("message").Attr("id", "m1"));
        await sm.OnOutgoingElementAsync(client, new XmppElement("message").Attr("id", "m2"));
        await sm.OnOutgoingElementAsync(client, new XmppElement("message").Attr("id", "m3"));

        Assert.Equal(3, sm.UnacknowledgedCount);

        // Stream features with SM
        var features = new XmppElement("stream:features")
            .Child(new XmppElement("sm", Xep0198StreamManagement.NsSm));

        var sentRaw = new List<XmppElement>();
        Func<XmppElement, Task> sendRaw = elem =>
        {
            sentRaw.Add(elem);
            return Task.CompletedTask;
        };

        // Server response: server has already handled up to h=2 (meaning m1 and m2 were received)
        var resumedResponse = new XmppElement("resumed", Xep0198StreamManagement.NsSm)
            .Attr("previd", "token-abc")
            .Attr("h", "2");

        Func<Task<XmppElement>> readNext = () => Task.FromResult(resumedResponse);

        IReadOnlyList<XmppElement>? retransmitted = null;
        sm.ResumedAndRetransmitted += stanzas => retransmitted = stanzas;

        var success = await sm.TryResumeAsync(client, features, sendRaw, readNext, CancellationToken.None);

        Assert.True(success);
        Assert.NotNull(retransmitted);
        // Only m3 should be retransmitted (not m1 or m2!)
        Assert.Single(retransmitted);
        Assert.Equal("m3", retransmitted[0].GetAttr("id"));

        // sentRaw contains: resume element + retransmitted m3
        Assert.Equal(2, sentRaw.Count);
        Assert.Equal("resume", sentRaw[0].Name);
        Assert.Equal("token-abc", sentRaw[0].GetAttr("previd"));
        Assert.Equal("m3", sentRaw[1].GetAttr("id"));
    }

    [Fact]
    public async Task TryResumeAsync_WhenFailed_DoesNotRetransmitAndReturnsFalse()
    {
        var sm = new Xep0198StreamManagement();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await sm.AttachAsync(client);

        // Enable SM
        await sm.OnIncomingElementAsync(client, new XmppElement("enabled", Xep0198StreamManagement.NsSm).Attr("id", "token-xyz"));

        // Queue a message
        await sm.OnOutgoingElementAsync(client, new XmppElement("message").Attr("id", "msg-fail"));

        var features = new XmppElement("stream:features")
            .Child(new XmppElement("sm", Xep0198StreamManagement.NsSm));

        var sentRaw = new List<XmppElement>();
        Func<XmppElement, Task> sendRaw = elem =>
        {
            sentRaw.Add(elem);
            return Task.CompletedTask;
        };

        // Server responds with <failed/>
        var failedResponse = new XmppElement("failed", Xep0198StreamManagement.NsSm)
            .Child(new XmppElement("item-not-found", "urn:ietf:params:xml:ns:xmpp-stanzas"));

        Func<Task<XmppElement>> readNext = () => Task.FromResult(failedResponse);

        var success = await sm.TryResumeAsync(client, features, sendRaw, readNext, CancellationToken.None);

        Assert.False(success);
        // Only resume element was sent
        Assert.Single(sentRaw);
        Assert.Equal("resume", sentRaw[0].Name);

        // Trigger OnResumptionFailedAsync
        IReadOnlyList<XmppElement>? reportedUnrecoverable = null;
        sm.UnrecoverableMessagesFailed += list => reportedUnrecoverable = list;

        await sm.OnResumptionFailedAsync(client, CancellationToken.None);

        Assert.NotNull(reportedUnrecoverable);
        Assert.Single(reportedUnrecoverable);
        Assert.Equal("msg-fail", reportedUnrecoverable[0].GetAttr("id"));
        Assert.False(sm.CanResume);
        Assert.Equal(0, sm.UnacknowledgedCount);
    }

    [Fact]
    public async Task OnStreamNegotiatedAsync_OnlyEnablesWhenServerAdvertisesSm()
    {
        var sm = new Xep0198StreamManagement();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await sm.AttachAsync(client);

        var sentRaw = new List<XmppElement>();
        Func<XmppElement, Task> sendRaw = elem =>
        {
            sentRaw.Add(elem);
            return Task.CompletedTask;
        };

        // Case A: Features without <sm/>
        var featuresWithoutSm = new XmppElement("stream:features")
            .Child(new XmppElement("bind", "urn:ietf:params:xml:ns:xmpp-bind"));

        await sm.OnStreamNegotiatedAsync(client, featuresWithoutSm, sendRaw, () => Task.FromResult(new XmppElement("dummy")), CancellationToken.None);

        Assert.False(sm.IsEnabled);
        Assert.Null(sm.ResumeId);
        Assert.Empty(sentRaw); // No enable was sent

        // Case B: Features with <sm/>
        var featuresWithSm = new XmppElement("stream:features")
            .Child(new XmppElement("bind", "urn:ietf:params:xml:ns:xmpp-bind"))
            .Child(new XmppElement("sm", Xep0198StreamManagement.NsSm));

        var enabledResponse = new XmppElement("enabled", Xep0198StreamManagement.NsSm)
            .Attr("id", "new-token-123")
            .Attr("resume", "true");

        await sm.OnStreamNegotiatedAsync(client, featuresWithSm, sendRaw, () => Task.FromResult(enabledResponse), CancellationToken.None);

        Assert.True(sm.IsEnabled);
        Assert.Equal("new-token-123", sm.ResumeId);
        Assert.Single(sentRaw);
        Assert.Equal("enable", sentRaw[0].Name);
    }
}
