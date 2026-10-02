using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.IntegrationTests.Infrastructure;
using Xunit;

namespace Stanza.IntegrationTests;

[Collection(XmppCollection.Name)]
[Trait("Category", "Integration")]
public class XmppClientLiveIntegrationTests(XmppContainerFixture fixture)
{
    [IntegrationFact]
    public async Task ConnectAndAuthenticate_WithStartTlsAndSasl_EstablishesReadySession()
    {
        var options = fixture.CreateClientOptions("usera", "password", "test-auth");
        await using var client = new XmppClient(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(cts.Token);

        Assert.True(client.IsConnected);
        Assert.Equal(XmppClientState.Ready, client.State);
        Assert.Equal("usera", client.BoundJid.LocalPart);
        Assert.Equal("localhost", client.BoundJid.Domain);
        Assert.Equal("test-auth", client.BoundJid.Resource);

        await client.DisconnectAsync();
        Assert.Equal(XmppClientState.Disconnected, client.State);
    }

    [IntegrationFact]
    public async Task PingServer_SendsIqPing_ReceivesResult()
    {
        var options = fixture.CreateClientOptions("usera", "password", "test-ping");
        await using var client = new XmppClient(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(cts.Token);

        var pingIq = IqStanza.CreateGet(Jid.Parse("localhost"));
        pingIq.RawElement.Child(new XmppElement("ping", "urn:xmpp:ping"));

        var response = await client.SendIqAsync(pingIq, cancellationToken: cts.Token);

        Assert.NotNull(response);
        Assert.True(response.IsResult);
        Assert.Equal(pingIq.Id, response.Id);

        await client.DisconnectAsync();
    }

    [IntegrationFact]
    public async Task SendPresence_PublishesAvailableState()
    {
        var options = fixture.CreateClientOptions("usera", "password", "test-presence");
        await using var client = new XmppClient(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(cts.Token);

        var presence = PresenceStanza.Available(show: "chat", status: "Testing integration with Prosody", priority: 5);
        await client.SendStanzaAsync(presence);

        // Verify client state remains healthy and connected
        Assert.True(client.IsConnected);
        Assert.Equal(XmppClientState.Ready, client.State);

        await client.DisconnectAsync();
    }

    [IntegrationFact]
    public async Task TwoPartyChat_SendsAndReceivesMessagesBetweenClients_Succeeds()
    {
        var optionsA = fixture.CreateClientOptions("usera", "password", "client-a");
        var optionsB = fixture.CreateClientOptions("userb", "password", "client-b");

        await using var clientA = new XmppClient(optionsA);
        await using var clientB = new XmppClient(optionsB);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await clientA.ConnectAsync(cts.Token);
        await clientB.ConnectAsync(cts.Token);

        Assert.True(clientA.IsConnected);
        Assert.True(clientB.IsConnected);

        var bReceivedTcs = new TaskCompletionSource<MessageStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientB.MessageReceived += msg =>
        {
            if (msg.Body == "Hello from Alice to Bob!")
            {
                bReceivedTcs.TrySetResult(msg);
            }
            return Task.CompletedTask;
        };

        var aReceivedTcs = new TaskCompletionSource<MessageStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientA.MessageReceived += msg =>
        {
            if (msg.Body == "Hello back from Bob to Alice!")
            {
                aReceivedTcs.TrySetResult(msg);
            }
            return Task.CompletedTask;
        };

        // Act 1: Alice sends to Bob
        var msgToBob = MessageStanza.CreateChat(clientB.BoundJid, "Hello from Alice to Bob!");
        await clientA.SendStanzaAsync(msgToBob);

        var receivedByBob = await bReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Hello from Alice to Bob!", receivedByBob.Body);
        Assert.Equal(clientA.BoundJid.ToString(), receivedByBob.From?.ToString());

        // Act 2: Bob replies to Alice
        var msgToAlice = MessageStanza.CreateChat(clientA.BoundJid, "Hello back from Bob to Alice!");
        await clientB.SendStanzaAsync(msgToAlice);

        var receivedByAlice = await aReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Hello back from Bob to Alice!", receivedByAlice.Body);
        Assert.Equal(clientB.BoundJid.ToString(), receivedByAlice.From?.ToString());

        await clientA.DisconnectAsync();
        await clientB.DisconnectAsync();
    }
}
