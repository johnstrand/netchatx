using System.IO.Pipelines;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.Gui.Services;
using Stanza.MockServer;
using Stanza.Storage.Models;
using Xunit;

namespace Stanza.Gui.Tests;

public class StreamManagementSessionTests
{
    private sealed class ReconnectingTransport : IXmppTransport
    {
        private LoopbackTransport _current = new();
        private readonly PipeWriter _blackHole = PipeWriter.Create(Stream.Null);

        public LoopbackTransport Current => _current;

        public PipeReader Input => _current.Input;
        public PipeWriter Output => DropOutgoing ? _blackHole : _current.Output;
        public bool IsSecure => _current.IsSecure;
        public bool DropOutgoing { get; set; }

        public LoopbackTransport SwitchToNewTransport()
        {
            DropOutgoing = false;
            _current = new LoopbackTransport();
            return _current;
        }

        public ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            return _current.ConnectAsync(host, port, cancellationToken);
        }

        public ValueTask UpgradeToTlsAsync(string targetHost, CancellationToken cancellationToken = default)
            => _current.UpgradeToTlsAsync(targetHost, cancellationToken);

        public ValueTask CloseAsync() => _current.CloseAsync();

        public ValueTask DisposeAsync() => _current.DisposeAsync();
    }

    [Fact]
    public async Task Session_EnablesStreamManagement_OnlyWhenAdvertisedByServer()
    {
        // 1. When server advertises SM
        var transport1 = new LoopbackTransport();
        await using var server1 = new MockXmppServer(transport1)
        {
            AdvertiseStreamManagement = true
        };
        server1.Start();

        var profile1 = new AccountProfile
        {
            Jid = "alice@mock.example.com",
            Password = "password123"
        };
        var client1 = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(profile1.Jid),
            Password = profile1.Password
        }, transport1);

        await using var session1 = new AccountSession(profile1, client1);
        var connected1 = await session1.ConnectAsync();

        Assert.True(connected1);
        Assert.NotNull(session1.StreamManagement);
        Assert.True(session1.StreamManagement.IsEnabled);
        Assert.NotNull(session1.StreamManagement.ResumeId);

        await session1.DisconnectAsync();

        // 2. When server does NOT advertise SM
        var transport2 = new LoopbackTransport();
        await using var server2 = new MockXmppServer(transport2)
        {
            AdvertiseStreamManagement = false
        };
        server2.Start();

        var profile2 = new AccountProfile
        {
            Jid = "bob@mock.example.com",
            Password = "password123"
        };
        var client2 = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(profile2.Jid),
            Password = profile2.Password
        }, transport2);

        await using var session2 = new AccountSession(profile2, client2);
        var connected2 = await session2.ConnectAsync();

        Assert.True(connected2);
        Assert.NotNull(session2.StreamManagement);
        Assert.False(session2.StreamManagement.IsEnabled);
        Assert.Null(session2.StreamManagement.ResumeId);

        await session2.DisconnectAsync();
    }

    [Fact]
    public async Task SimulatedTransientDisconnect_ResumesAndRetransmitsUnacknowledgedStanzasWithoutDuplicates()
    {
        var transport = new ReconnectingTransport();
        await using var server = new MockXmppServer(transport.Current)
        {
            AdvertiseStreamManagement = true,
            AllowResumption = true,
            CurrentResumeId = "resumable-session-token-42"
        };
        server.Start();

        var receivedMessages = new List<MessageStanza>();
        server.OnMessageReceived += msg =>
        {
            lock (receivedMessages)
            {
                receivedMessages.Add(msg);
            }
        };

        var profile = new AccountProfile
        {
            Jid = "alice@mock.example.com",
            Password = "password123"
        };
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(profile.Jid),
            Password = profile.Password
        }, transport);

        await using var session = new AccountSession(profile, client);
        var connected = await session.ConnectAsync();
        Assert.True(connected);
        Assert.NotNull(session.StreamManagement);
        Assert.True(session.StreamManagement.IsEnabled);
        Assert.Equal("resumable-session-token-42", session.StreamManagement.ResumeId);

        // Send Msg1
        var msg1 = new MessageStanza(to: Jid.Parse("bob@mock.example.com"), body: "Message One", type: MessageStanza.TypeChat);
        msg1.Id = "m1";
        await client.SendStanzaAsync(msg1);

        // Wait for server to receive Msg1
        await Task.Delay(100);
        Assert.Equal(1u, server.ServerInboundHandled);

        // Server acknowledges Msg1 (h = 1)
        await server.InjectElementAsync(new XmppElement("a", "urn:xmpp:sm:3").Attr("h", "1"));
        await Task.Delay(50);
        Assert.Equal(0, session.StreamManagement.UnacknowledgedCount);

        // Simulate transient network drop before server receives/handles Msg2
        // Client sends Msg2 but outgoing network drops it in flight
        transport.DropOutgoing = true;
        var msg2 = new MessageStanza(to: Jid.Parse("bob@mock.example.com"), body: "Message Two", type: MessageStanza.TypeChat);
        msg2.Id = "m2";
        await client.SendStanzaAsync(msg2);

        // Close transport
        await transport.CloseAsync();

        // Wait for client to detect disconnect
        var disconnectedTcs = new TaskCompletionSource<bool>();
        session.Client.StateChanged += state =>
        {
            if (state == XmppClientState.Disconnected)
            {
                disconnectedTcs.TrySetResult(true);
            }
        };
        if (session.Client.State != XmppClientState.Disconnected)
        {
            await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        // Verify session preserved resumption token and unacknowledged Msg2
        Assert.True(session.StreamManagement.CanResume);
        Assert.Equal(1, session.StreamManagement.UnacknowledgedCount);
        Assert.Equal("m2", session.StreamManagement.UnacknowledgedStanzas[0].GetAttr("id"));

        // Server still only handled 1 message before drop
        server.ServerInboundHandled = 1;

        // Switch to new transport and restart mock server loop
        var newLoopback = transport.SwitchToNewTransport();
        server.ResetTransport(newLoopback);

        // Reconnect session
        var reconnected = await session.ConnectAsync();
        Assert.True(reconnected, $"Reconnection failed: {session.LastErrorMessage}");
        Assert.True(client.IsReady);
        Assert.Equal(AccountConnectionState.Connected, session.ConnectionState);

        // Wait for retransmitted message to arrive at mock server
        await Task.Delay(100);

        // Server should now have handled both Msg1 and Msg2
        Assert.Equal(2u, server.ServerInboundHandled);

        lock (receivedMessages)
        {
            Assert.Equal(2, receivedMessages.Count);
            Assert.Equal("Message One", receivedMessages[0].Body);
            Assert.Equal("Message Two", receivedMessages[1].Body);
        }

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task ResumeFailure_FallsBackToFreshAuthenticatedSession_AndReportsUnrecoverableMessages()
    {
        var transport = new ReconnectingTransport();
        await using var server = new MockXmppServer(transport.Current)
        {
            AdvertiseStreamManagement = true,
            AllowResumption = true,
            CurrentResumeId = "initial-token-1"
        };
        server.Start();

        var profile = new AccountProfile
        {
            Jid = "alice@mock.example.com",
            Password = "password123"
        };
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(profile.Jid),
            Password = profile.Password
        }, transport);

        await using var session = new AccountSession(profile, client);
        var connected = await session.ConnectAsync();
        Assert.True(connected);

        // Send an in-flight message that won't be acked
        var msg = new MessageStanza(to: Jid.Parse("bob@mock.example.com"), body: "Unrecovered message", type: MessageStanza.TypeChat);
        msg.Id = "lost-msg-1";
        await client.SendStanzaAsync(msg);

        // Simulate disconnect
        await transport.CloseAsync();

        // Wait for client to transition to Disconnected
        var disconnectedTcs = new TaskCompletionSource<bool>();
        session.Client.StateChanged += state =>
        {
            if (state == XmppClientState.Disconnected)
            {
                disconnectedTcs.TrySetResult(true);
            }
        };
        if (session.Client.State != XmppClientState.Disconnected)
        {
            await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        IReadOnlyList<XmppElement>? unrecoverableReported = null;
        session.UnrecoverableMessagesFailed += list => unrecoverableReported = list;

        // Mock server rejects resumption (e.g. resumption session expired on server)
        server.AllowResumption = false;
        server.CurrentResumeId = "fresh-token-2";

        var newLoopback = transport.SwitchToNewTransport();
        server.ResetTransport(newLoopback);

        // Reconnect session: resumption will fail and cleanly fall back to fresh SASL auth
        var reconnected = await session.ConnectAsync();
        Assert.True(reconnected);
        Assert.True(client.IsReady);
        Assert.Equal(AccountConnectionState.Connected, session.ConnectionState);

        // Verify unrecoverable messages were reported
        Assert.NotNull(unrecoverableReported);
        Assert.Single(unrecoverableReported);
        Assert.Equal("lost-msg-1", unrecoverableReported[0].GetAttr("id"));
        Assert.Contains("1 unacknowledged message(s) could not be recovered", session.LastErrorMessage);

        // Fresh session has fresh stream management enabled
        Assert.True(session.StreamManagement!.IsEnabled);
        Assert.Equal("fresh-token-2", session.StreamManagement.ResumeId);

        await session.DisconnectAsync();
    }
}
