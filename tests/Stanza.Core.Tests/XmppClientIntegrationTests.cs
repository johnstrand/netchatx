using System.IO.Pipelines;
using System.Text;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.MockServer;
using Xunit;

namespace Stanza.Core.Tests;

public class XmppClientIntegrationTests
{
    private sealed class ReconnectableLoopbackTransport(
        Func<LoopbackTransport, int, Task> onConnect,
        int failedConnectAttempts = 0) : IXmppTransport
    {
        private LoopbackTransport _current = new();
        private int _connectAttempts;

        public PipeReader Input => _current.Input;
        public PipeWriter Output => _current.Output;
        public bool IsSecure => _current.IsSecure;

        public async ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _connectAttempts);
            _current = new LoopbackTransport();

            if (attempt <= failedConnectAttempts)
            {
                throw new IOException("Simulated connection failure.");
            }

            await _current.ConnectAsync(host, port, cancellationToken);
            await onConnect(_current, attempt);
        }

        public ValueTask UpgradeToTlsAsync(string targetHost, CancellationToken cancellationToken = default)
            => _current.UpgradeToTlsAsync(targetHost, cancellationToken);

        public ValueTask CloseAsync() => _current.CloseAsync();

        public ValueTask DisposeAsync() => _current.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_WhenTransportConnectionFails_CanRetryWithSameClient()
    {
        var servers = new List<MockXmppServer>();
        var transport = new ReconnectableLoopbackTransport((loopback, _) =>
        {
            var server = new MockXmppServer(loopback);
            servers.Add(server);
            server.Start();
            return Task.CompletedTask;
        }, failedConnectAttempts: 1);
        await using var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transport);

        try
        {
            await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync());
            Assert.Equal(XmppClientState.Disconnected, client.State);

            await client.ConnectAsync();

            Assert.Equal(XmppClientState.Ready, client.State);
        }
        finally
        {
            await client.DisconnectAsync();
            foreach (var server in servers)
            {
                await server.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ConnectAsync_WhenStreamNegotiationFails_CanRetryWithSameClient()
    {
        var servers = new List<MockXmppServer>();
        var transport = new ReconnectableLoopbackTransport(async (loopback, attempt) =>
        {
            if (attempt == 1)
            {
                _ = Task.Run(async () =>
                {
                    var parser = new XmppStreamParser();
                    _ = await parser.ReadElementAsync(loopback.ServerInput);
                    var invalidFeatures = "<stream:stream from='mock.example.com' id='s-1' version='1.0' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'><stream:features/>";
                    await loopback.ServerOutput.WriteAsync(Encoding.UTF8.GetBytes(invalidFeatures));
                    await loopback.ServerOutput.FlushAsync();
                });
                return;
            }

            var server = new MockXmppServer(loopback);
            servers.Add(server);
            server.Start();
            await Task.CompletedTask;
        });
        await using var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transport);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
            Assert.Contains("No SASL mechanisms offered", exception.Message);
            Assert.Equal(XmppClientState.Disconnected, client.State);

            await client.ConnectAsync();

            Assert.Equal(XmppClientState.Ready, client.State);
        }
        finally
        {
            await client.DisconnectAsync();
            foreach (var server in servers)
            {
                await server.DisposeAsync();
            }
        }
    }

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

    [Fact]
    public async Task XmppClient_BuffersEarlyMessages_DispatchedWhenHandlerAttached()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        };

        await using var client = new XmppClient(options, transport);
        await client.ConnectAsync();

        // Server injects a message BEFORE client attaches MessageReceived handler
        var earlyMsg = new MessageStanza(to: Jid.Parse("alice@mock.example.com"), type: MessageStanza.TypeChat)
        {
            From = Jid.Parse("bob@mock.example.com"),
            Body = "Buffered offline message"
        };
        await server.InjectStanzaAsync(earlyMsg);

        // Give loopback reader a moment to pump and buffer the message
        await Task.Delay(50);

        // Now client attaches MessageReceived
        MessageStanza? dispatchedMsg = null;
        var tcs = new TaskCompletionSource<MessageStanza>();
        client.MessageReceived += msg =>
        {
            dispatchedMsg = msg;
            tcs.TrySetResult(msg);
            return Task.CompletedTask;
        };

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(result);
        Assert.Equal("Buffered offline message", result.Body);
        Assert.Equal("bob@mock.example.com", result.From?.ToString());
    }

    [Fact]
    public async Task LiveDockerContainer_WhenRunning_CanConnectAndAuthenticate()
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        try
        {
            await tcp.ConnectAsync("127.0.0.1", 5222).WaitAsync(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Container not running in this environment, skip gracefully
            return;
        }
        tcp.Close();

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse("usera@localhost"),
            Password = "password",
            Host = "127.0.0.1",
            Port = 5222,
            AllowUntrustedCertificates = true
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var client = new XmppClient(options);
        await client.ConnectAsync(cts.Token);

        Assert.True(client.IsConnected);
        Assert.Equal("usera", client.BoundJid.LocalPart);
        Assert.Equal("localhost", client.BoundJid.Domain);
    }
}
