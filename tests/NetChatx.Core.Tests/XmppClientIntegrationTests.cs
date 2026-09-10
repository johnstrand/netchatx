using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Transport;
using NetChatx.Core.Xml;
using NetChatx.MockServer;
using Xunit;

namespace NetChatx.Core.Tests;

public class XmppClientIntegrationTests
{
    [Fact]
    public async Task ConnectAndExchangeMessages_ViaLoopback_Succeeds()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123",
            Resource = "CustomRes"
        };

        await using var client = new XmppClient(options, transport);

        MessageStanza? serverReceivedMsg = null;
        var serverMsgTcs = new TaskCompletionSource<MessageStanza>();
        server.OnMessageReceived += msg =>
        {
            serverReceivedMsg = msg;
            serverMsgTcs.TrySetResult(msg);
        };

        MessageStanza? clientReceivedMsg = null;
        var clientMsgTcs = new TaskCompletionSource<MessageStanza>();
        client.MessageReceived += msg =>
        {
            clientReceivedMsg = msg;
            clientMsgTcs.TrySetResult(msg);
            return Task.CompletedTask;
        };

        // Act: Connect
        await client.ConnectAsync();

        // Assert: Ready state & Resource Bound
        Assert.Equal(XmppClientState.Ready, client.State);
        Assert.Equal("alice@mock.example.com/CustomRes", client.BoundJid.ToString());

        // Act: Client sends message to server
        var outgoingMsg = MessageStanza.CreateChat(Jid.Parse("bob@mock.example.com"), "Hello from Alice!");
        await client.SendStanzaAsync(outgoingMsg);

        // Wait for server to receive
        var receivedByServer = await serverMsgTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Hello from Alice!", receivedByServer.Body);
        Assert.Equal("bob@mock.example.com", receivedByServer.To?.ToString());

        // Act: Server sends message to client
        var inboundMsg = MessageStanza.CreateChat(client.BoundJid, "Hello back from Bob!", Jid.Parse("bob@mock.example.com"));
        await server.InjectStanzaAsync(inboundMsg);

        // Wait for client to receive
        var receivedByClient = await clientMsgTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Hello back from Bob!", receivedByClient.Body);
        Assert.Equal("bob@mock.example.com", receivedByClient.From?.ToString());

        // Act: Ping IQ exchange
        var pingIq = IqStanza.CreateGet(Jid.Parse("mock.example.com"));
        pingIq.RawElement.Child(new XmppElement("ping", "urn:xmpp:ping"));
        var pingResult = await client.SendIqAsync(pingIq);

        Assert.True(pingResult.IsResult);
        Assert.Equal(pingIq.Id, pingResult.Id);
    }

    [Fact]
    public async Task RunReadLoopAsync_OnTransportError_TransitionsToDisconnectedState()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123",
            Resource = "CustomRes"
        };

        await using var client = new XmppClient(options, transport);

        var stateChanges = new List<XmppClientState>();
        var disconnectedTcs = new TaskCompletionSource<bool>();

        client.StateChanged += state =>
        {
            stateChanges.Add(state);
            if (state == XmppClientState.Disconnected)
            {
                disconnectedTcs.TrySetResult(true);
            }
        };

        // Act: Connect client
        await client.ConnectAsync();
        Assert.Equal(XmppClientState.Ready, client.State);

        // Inject transport error by faulting the server's output writer pipe
        await transport.ServerOutput.CompleteAsync(new IOException("Simulated network drop"));

        // Wait for read loop to detect fault and transition state to Disconnected
        var disconnected = await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(disconnected);
        Assert.Equal(XmppClientState.Disconnected, client.State);
        Assert.Contains(XmppClientState.Disconnected, stateChanges);
    }
}
