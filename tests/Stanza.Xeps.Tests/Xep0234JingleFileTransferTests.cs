using System.Text;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.MockServer;
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

    [Fact]
    public async Task EndToEnd_TwoClients_TransferFileViaIbb_Succeeds()
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

        var jingleA = new Xep0234JingleFileTransfer();
        var jingleB = new Xep0234JingleFileTransfer();

        await jingleA.AttachAsync(clientA);
        await jingleB.AttachAsync(clientB);

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        var payload = Encoding.UTF8.GetBytes(new string('X', 5000));
        var progressRecordedA = new List<double>();
        var progressRecordedB = new List<double>();
        var bobReceivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aliceCompletedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        jingleA.TransferProgress += p => progressRecordedA.Add(p.Percent);
        jingleB.TransferProgress += p => progressRecordedB.Add(p.Percent);

        jingleA.TransferCompleted += (session, data) =>
        {
            aliceCompletedTcs.TrySetResult(true);
        };

        jingleB.TransferOfferReceived += async session =>
        {
            await Task.Delay(20);
            await jingleB.AcceptOfferAsync(session.SessionId);
        };

        jingleB.TransferCompleted += (session, data) =>
        {
            bobReceivedTcs.TrySetResult(data);
        };

        var sessionA = await jingleA.SendFileOfferAsync(
            clientB.BoundJid!,
            "testfile.txt",
            payload,
            "text/plain",
            blockSize: 1024);

        var receivedData = await bobReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await aliceCompletedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(payload, receivedData);
        Assert.Equal(FileTransferState.Completed, sessionA.State);
        Assert.NotEmpty(progressRecordedA);
        Assert.NotEmpty(progressRecordedB);
        Assert.Equal(1.0, progressRecordedA[^1]);
        Assert.Equal(1.0, progressRecordedB[^1]);

        await clientA.DisconnectAsync();
        await clientB.DisconnectAsync();
    }

    [Fact]
    public async Task RejectOffer_SetsStateToRejected_AndNotifiesPeer()
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

        var jingleA = new Xep0234JingleFileTransfer();
        var jingleB = new Xep0234JingleFileTransfer();

        await jingleA.AttachAsync(clientA);
        await jingleB.AttachAsync(clientB);

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        var payload = Encoding.UTF8.GetBytes("Small payload");
        var rejectedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        jingleA.TransferTerminated += (session, reason) =>
        {
            rejectedTcs.TrySetResult(reason);
        };

        var bobRejectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        jingleB.TransferOfferReceived += async session =>
        {
            try
            {
                await Task.Delay(20);
                await jingleB.RejectOfferAsync(session.SessionId, "decline");
                bobRejectedTcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                bobRejectedTcs.TrySetException(ex);
            }
        };

        var sessionA = await jingleA.SendFileOfferAsync(
            clientB.BoundJid!,
            "rejected.txt",
            payload,
            "text/plain");

        var reason = await rejectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await bobRejectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("decline", reason);
        Assert.Equal(FileTransferState.Rejected, sessionA.State);

        await clientA.DisconnectAsync();
        await clientB.DisconnectAsync();
    }

    [Fact]
    public async Task CancelTransfer_DuringActiveStream_TerminatesGracefully()
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

        var jingleA = new Xep0234JingleFileTransfer();
        var jingleB = new Xep0234JingleFileTransfer();

        await jingleA.AttachAsync(clientA);
        await jingleB.AttachAsync(clientB);

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        var payload = Encoding.UTF8.GetBytes(new string('Z', 20000));
        var bTerminatedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aCancelledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        jingleB.TransferOfferReceived += async session =>
        {
            await Task.Delay(20);
            await jingleB.AcceptOfferAsync(session.SessionId);
        };

        jingleB.TransferTerminated += (session, reason) =>
        {
            bTerminatedTcs.TrySetResult(reason);
        };

        jingleA.TransferProgress += async p =>
        {
            if (p.BytesTransferred > 1000 && !aCancelledTcs.Task.IsCompleted)
            {
                try
                {
                    await jingleA.CancelTransferAsync(p.SessionId);
                    aCancelledTcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    aCancelledTcs.TrySetException(ex);
                }
            }
        };

        var sessionA = await jingleA.SendFileOfferAsync(
            clientB.BoundJid!,
            "large.bin",
            payload,
            "application/octet-stream",
            blockSize: 512);

        var reason = await bTerminatedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await aCancelledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("cancel", reason);
        Assert.Equal(FileTransferState.Cancelled, sessionA.State);

        await clientA.DisconnectAsync();
        await clientB.DisconnectAsync();
    }
}
