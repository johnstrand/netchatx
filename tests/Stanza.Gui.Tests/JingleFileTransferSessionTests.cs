using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.Gui.ViewModels;
using Stanza.MockServer;
using Stanza.Storage;
using Xunit;

namespace Stanza.Gui.Tests;

public class JingleFileTransferSessionTests : IDisposable
{
    private readonly string _dbPathAlice;
    private readonly string _dbPathBob;
    private readonly DatabaseContext _dbContextAlice;
    private readonly DatabaseContext _dbContextBob;

    public JingleFileTransferSessionTests()
    {
        _dbPathAlice = $"test_jingle_alice_{Guid.NewGuid():N}.db";
        _dbPathBob = $"test_jingle_bob_{Guid.NewGuid():N}.db";
        _dbContextAlice = new DatabaseContext(_dbPathAlice);
        _dbContextBob = new DatabaseContext(_dbPathBob);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPathAlice)) File.Delete(_dbPathAlice); } catch { }
        try { if (File.Exists(_dbPathBob)) File.Delete(_dbPathBob); } catch { }
    }

    [AvaloniaFact]
    public async Task EndToEnd_TwoClients_TransferFileViaJingle_SucceedsWithProgressAndFileSaved()
    {
        var transportA = new LoopbackTransport();
        var transportB = new LoopbackTransport();

        await using var serverA = new MockXmppServer(transportA);
        await using var serverB = new MockXmppServer(transportB);

        serverA.PeerServer = serverB;
        serverB.PeerServer = serverA;

        serverA.Start();
        serverB.Start();

        var clientA = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transportA);

        var clientB = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("bob@mock.example.com"),
            Password = "password123"
        }, transportB);

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        var mainVmA = new MainChatViewModel(clientA, _dbContextAlice, () => Task.CompletedTask);
        await mainVmA.InitializeAsync();

        var mainVmB = new MainChatViewModel(clientB, _dbContextBob, () => Task.CompletedTask);
        await mainVmB.InitializeAsync();

        var peerBob = Jid.Parse("bob@mock.example.com");
        var peerAlice = Jid.Parse("alice@mock.example.com");

        var convAlice = mainVmA.GetOrCreateConversation("bob@mock.example.com", "Bob", peerBob, isGroupChat: false);

        var payload = Encoding.UTF8.GetBytes(new string('X', 5000));
        await convAlice.UploadOrCacheImageAsync(payload, "test_data.txt");

        // Wait for Alice's outbound file transfer bubble
        for (int i = 0; i < 50; i++)
        {
            if (convAlice.Messages.Count > 0) break;
            await Task.Delay(50);
        }

        Assert.Single(convAlice.Messages);
        var bubbleAlice = convAlice.Messages[0];
        Assert.True(bubbleAlice.HasFileTransfer);
        Assert.Equal("test_data.txt", bubbleAlice.FileTransferName);
        Assert.Equal(payload.Length, bubbleAlice.FileTransferSize);

        // Wait for Bob to receive the offer
        for (int i = 0; i < 50; i++)
        {
            var c = mainVmB.Conversations.FirstOrDefault(x => x.RemoteJid.EqualsBare(peerAlice));
            if (c != null && c.Messages.Count > 0) break;
            await Task.Delay(50);
        }

        var convBob = mainVmB.GetOrCreateConversation("alice@mock.example.com", "Alice", peerAlice, isGroupChat: false);
        Assert.Single(convBob.Messages);
        var bubbleBob = convBob.Messages[0];
        Assert.True(bubbleBob.HasFileTransfer);
        Assert.True(bubbleBob.IsFileTransferPending);
        Assert.Equal("test_data.txt", bubbleBob.FileTransferName);
        Assert.Equal(payload.Length, bubbleBob.FileTransferSize);

        // Bob accepts the transfer
        await bubbleBob.AcceptFileTransferCommand.ExecuteAsync(null);

        // Wait for both sides to complete transfer
        for (int i = 0; i < 100; i++)
        {
            if (bubbleBob.IsFileTransferCompleted && bubbleAlice.IsFileTransferCompleted) break;
            await Task.Delay(50);
        }

        Assert.True(bubbleBob.IsFileTransferCompleted);
        Assert.False(bubbleBob.IsFileTransferActive);
        Assert.Equal(100.0, bubbleBob.FileTransferProgress);

        Assert.True(bubbleAlice.IsFileTransferCompleted);
        Assert.False(bubbleAlice.IsFileTransferActive);

        // Verify the file was saved on Bob's side
        Assert.NotNull(bubbleBob.FileTransferPath);
        Assert.True(File.Exists(bubbleBob.FileTransferPath));
        var receivedBytes = await File.ReadAllBytesAsync(bubbleBob.FileTransferPath);
        Assert.Equal(payload, receivedBytes);

        try { File.Delete(bubbleBob.FileTransferPath); } catch { }
    }

    [AvaloniaFact]
    public async Task InboundOffer_Rejected_BothSidesSeeTerminalStatus()
    {
        var transportA = new LoopbackTransport();
        var transportB = new LoopbackTransport();

        await using var serverA = new MockXmppServer(transportA);
        await using var serverB = new MockXmppServer(transportB);

        serverA.PeerServer = serverB;
        serverB.PeerServer = serverA;

        serverA.Start();
        serverB.Start();

        var clientA = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transportA);

        var clientB = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("bob@mock.example.com"),
            Password = "password123"
        }, transportB);

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        var mainVmA = new MainChatViewModel(clientA, _dbContextAlice, () => Task.CompletedTask);
        await mainVmA.InitializeAsync();

        var mainVmB = new MainChatViewModel(clientB, _dbContextBob, () => Task.CompletedTask);
        await mainVmB.InitializeAsync();

        var peerBob = Jid.Parse("bob@mock.example.com");
        var peerAlice = Jid.Parse("alice@mock.example.com");

        var convAlice = mainVmA.GetOrCreateConversation("bob@mock.example.com", "Bob", peerBob, isGroupChat: false);

        var payload = Encoding.UTF8.GetBytes("Data to decline");
        await convAlice.UploadOrCacheImageAsync(payload, "rejected.txt");

        // Wait for Alice's outbound bubble
        for (int i = 0; i < 50; i++)
        {
            if (convAlice.Messages.Count > 0) break;
            await Task.Delay(50);
        }

        // Wait for Bob to receive the offer
        for (int i = 0; i < 50; i++)
        {
            var c = mainVmB.Conversations.FirstOrDefault(x => x.RemoteJid.EqualsBare(peerAlice));
            if (c != null && c.Messages.Count > 0) break;
            await Task.Delay(50);
        }

        var convBob = mainVmB.GetOrCreateConversation("alice@mock.example.com", "Alice", peerAlice, isGroupChat: false);
        Assert.Single(convBob.Messages);
        var bubbleBob = convBob.Messages[0];
        Assert.True(bubbleBob.IsFileTransferPending);

        // Bob declines the transfer
        await bubbleBob.RejectFileTransferCommand.ExecuteAsync(null);
        await Task.Delay(50);

        Assert.False(bubbleBob.IsFileTransferPending);
        Assert.Contains("declined", bubbleBob.FileTransferStatus, StringComparison.OrdinalIgnoreCase);

        // Wait for Alice to receive terminal decline
        var bubbleAlice = convAlice.Messages[0];
        for (int i = 0; i < 50; i++)
        {
            if (bubbleAlice.FileTransferStatus?.Contains("declined", StringComparison.OrdinalIgnoreCase) == true) break;
            await Task.Delay(50);
        }

        Assert.False(bubbleAlice.IsFileTransferActive);
        Assert.Contains("declined", bubbleAlice.FileTransferStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task ActiveTransfer_CancelledBySender_BothSidesSeeTerminalStatus()
    {
        var transportA = new LoopbackTransport();
        var transportB = new LoopbackTransport();

        await using var serverA = new MockXmppServer(transportA);
        await using var serverB = new MockXmppServer(transportB);

        serverA.PeerServer = serverB;
        serverB.PeerServer = serverA;

        serverA.Start();
        serverB.Start();

        var clientA = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transportA);

        var clientB = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("bob@mock.example.com"),
            Password = "password123"
        }, transportB);

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        var mainVmA = new MainChatViewModel(clientA, _dbContextAlice, () => Task.CompletedTask);
        await mainVmA.InitializeAsync();

        var mainVmB = new MainChatViewModel(clientB, _dbContextBob, () => Task.CompletedTask);
        await mainVmB.InitializeAsync();

        var peerBob = Jid.Parse("bob@mock.example.com");
        var peerAlice = Jid.Parse("alice@mock.example.com");

        var convAlice = mainVmA.GetOrCreateConversation("bob@mock.example.com", "Bob", peerBob, isGroupChat: false);

        var payload = Encoding.UTF8.GetBytes(new string('Y', 50000));
        await convAlice.UploadOrCacheImageAsync(payload, "cancelled.txt");

        // Wait for Alice's outbound bubble
        for (int i = 0; i < 50; i++)
        {
            if (convAlice.Messages.Count > 0) break;
            await Task.Delay(50);
        }

        // Wait for Bob to receive the offer
        for (int i = 0; i < 50; i++)
        {
            var c = mainVmB.Conversations.FirstOrDefault(x => x.RemoteJid.EqualsBare(peerAlice));
            if (c != null && c.Messages.Count > 0) break;
            await Task.Delay(50);
        }

        var convBob = mainVmB.GetOrCreateConversation("alice@mock.example.com", "Alice", peerAlice, isGroupChat: false);
        Assert.Single(convBob.Messages);
        var bubbleBob = convBob.Messages[0];

        // Bob accepts the transfer
        await bubbleBob.AcceptFileTransferCommand.ExecuteAsync(null);

        // While active, Alice cancels
        var bubbleAlice = convAlice.Messages[0];
        await bubbleAlice.CancelFileTransferCommand.ExecuteAsync(null);
        await Task.Delay(50);

        Assert.False(bubbleAlice.IsFileTransferActive);
        Assert.Contains("cancelled", bubbleAlice.FileTransferStatus, StringComparison.OrdinalIgnoreCase);

        // Wait for Bob to see terminal cancelled status
        for (int i = 0; i < 50; i++)
        {
            if (bubbleBob.FileTransferStatus?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true) break;
            await Task.Delay(50);
        }

        Assert.False(bubbleBob.IsFileTransferActive);
        Assert.Contains("cancelled", bubbleBob.FileTransferStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task FailedJingleNegotiation_FallsBackSafely_WithoutLosingAttachment()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);

        server.OnIqReceived += iq =>
        {
            if (iq.RawElement.Element("jingle", "urn:xmpp:jingle:1") is not null)
            {
                var errorIq = iq.CreateError("service-unavailable");
                _ = server.InjectStanzaAsync(errorIq);
            }
        };

        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContextAlice, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var offlinePeer = Jid.Parse("offline@mock.example.com");
        var conv = mainVm.GetOrCreateConversation("offline@mock.example.com", "Offline User", offlinePeer, isGroupChat: false);

        var payload = Encoding.UTF8.GetBytes("Test fallback image data");

        // When Jingle negotiation fails or is unsupported, it falls back safely to local cache without losing attachment
        var resultUrl = await conv.UploadOrCacheImageAsync(payload, "photo.png");

        Assert.NotNull(resultUrl);
        Assert.StartsWith("file://", resultUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jingle negotiation failed", conv.FileTransferStatusMessage);
    }
}
