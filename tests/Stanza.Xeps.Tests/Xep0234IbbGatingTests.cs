using System.Text;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Sharing;
using Xunit;

namespace Stanza.Xeps.Tests;

public sealed class Xep0234IbbGatingTests
{
    [Fact]
    public async Task HandleIbb_OpenBeforeConsent_RejectsWithUnexpectedRequest_AndDoesNotTransitionState()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("bob@mock.example.com/laptop"),
            Password = "password123"
        }, transport);

        var feature = new Xep0234JingleFileTransfer();
        await feature.AttachAsync(client);
        await client.ConnectAsync();

        var started = false;
        feature.TransferStarted += _ => started = true;

        // Peer offers file transfer
        var initiateIq = XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='init-1'>" +
            "<jingle xmlns='urn:xmpp:jingle:1' action='session-initiate' sid='sid-ibb-gating-1'>" +
            "<description xmlns='urn:xmpp:jingle:apps:file-transfer:5'><file><name>secret.pdf</name><size>1000</size></file></description>" +
            "<transport xmlns='http://jabber.org/protocol/ibb' block-size='4096'/>" +
            "</jingle></iq>");
        await feature.OnIncomingElementAsync(client, initiateIq);

        var session = feature.GetSession("sid-ibb-gating-1");
        Assert.NotNull(session);
        Assert.Equal(FileTransferState.Pending, session!.State);

        var openRespTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "open-1") openRespTcs.TrySetResult(iq);
        };

        // Peer sends <open/> before local user calls AcceptOfferAsync
        var prematureOpenIq = new IqStanza(XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='open-1'>" +
            "<open xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-1' block-size='4096'/>" +
            "</iq>"));
        await feature.OnIncomingElementAsync(client, prematureOpenIq.RawElement);

        var response = await openRespTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // State must remain Pending, transfer must NOT start, and unexpected-request error returned
        Assert.Equal(FileTransferState.Pending, session.State);
        Assert.False(started);
        Assert.True(response.IsError);
        Assert.Equal("open-1", response.Id);
        var errElem = response.RawElement.Element("error");
        Assert.NotNull(errElem);
        Assert.NotNull(errElem!.Element("unexpected-request", "urn:ietf:params:xml:ns:xmpp-stanzas"));

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task HandleIbb_OpenFromUnauthorizedPeer_RejectsWithForbidden_AndDoesNotTransitionState()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("bob@mock.example.com/laptop"),
            Password = "password123"
        }, transport);

        // Auto-answer session-accept IQ sets from client
        server.OnIqReceived += iq =>
        {
            if (iq.IsSet && iq.RawElement.Element("jingle", "urn:xmpp:jingle:1") is not null)
            {
                _ = server.InjectStanzaAsync(iq.CreateResult());
            }
        };

        var feature = new Xep0234JingleFileTransfer();
        await feature.AttachAsync(client);
        await client.ConnectAsync();

        var started = false;
        feature.TransferStarted += _ => started = true;

        var initiateIq = XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='init-2'>" +
            "<jingle xmlns='urn:xmpp:jingle:1' action='session-initiate' sid='sid-ibb-gating-2'>" +
            "<description xmlns='urn:xmpp:jingle:apps:file-transfer:5'><file><name>secret.pdf</name><size>1000</size></file></description>" +
            "</jingle></iq>");
        await feature.OnIncomingElementAsync(client, initiateIq);

        // User accepts offer
        await feature.AcceptOfferAsync("sid-ibb-gating-2");
        var session = feature.GetSession("sid-ibb-gating-2");
        Assert.NotNull(session);
        Assert.Equal(FileTransferState.Accepted, session!.State);

        var malloryIqTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "open-mallory")
            {
                malloryIqTcs.TrySetResult(iq);
            }
        };

        // Mallory attempts to inject <open/>
        var spoofedOpenIq = new IqStanza(XmppElement.Parse(
            "<iq type='set' from='mallory@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='open-mallory'>" +
            "<open xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-2' block-size='4096'/>" +
            "</iq>"));
        await feature.OnIncomingElementAsync(client, spoofedOpenIq.RawElement);

        var response = await malloryIqTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // State remains Accepted, transfer did not start, forbidden returned
        Assert.Equal(FileTransferState.Accepted, session.State);
        Assert.False(started);
        Assert.True(response.IsError);
        Assert.Equal("open-mallory", response.Id);
        var errElem = response.RawElement.Element("error");
        Assert.NotNull(errElem);
        Assert.NotNull(errElem!.Element("forbidden", "urn:ietf:params:xml:ns:xmpp-stanzas"));

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task HandleIbb_TrafficLifecycle_AuthorizedAndGatedProperly()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("bob@mock.example.com/laptop"),
            Password = "password123"
        }, transport);

        server.OnIqReceived += iq =>
        {
            if (iq.IsSet && iq.RawElement.Element("jingle", "urn:xmpp:jingle:1") is not null)
            {
                _ = server.InjectStanzaAsync(iq.CreateResult());
            }
        };

        var feature = new Xep0234JingleFileTransfer();
        await feature.AttachAsync(client);
        await client.ConnectAsync();

        var started = false;
        feature.TransferStarted += _ => started = true;

        var initiateIq = XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='init-3'>" +
            "<jingle xmlns='urn:xmpp:jingle:1' action='session-initiate' sid='sid-ibb-gating-3'>" +
            "<description xmlns='urn:xmpp:jingle:apps:file-transfer:5'><file><name>data.bin</name><size>4</size></file></description>" +
            "</jingle></iq>");
        await feature.OnIncomingElementAsync(client, initiateIq);

        var session = feature.GetSession("sid-ibb-gating-3");
        Assert.NotNull(session);

        // 1. Premature data while Pending is rejected
        var prematureDataTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "data-premature") prematureDataTcs.TrySetResult(iq);
        };

        var prematureData = XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='data-premature'>" +
            "<data xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-3' seq='0'>AQIDBA==</data>" +
            "</iq>");
        await feature.OnIncomingElementAsync(client, prematureData);

        var prematureResp = await prematureDataTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(FileTransferState.Pending, session!.State);
        Assert.Equal(0, session.BytesTransferred);
        Assert.True(prematureResp.IsError);
        Assert.NotNull(prematureResp.RawElement.Element("error")?.Element("unexpected-request", "urn:ietf:params:xml:ns:xmpp-stanzas"));

        // 2. Accept offer
        await feature.AcceptOfferAsync("sid-ibb-gating-3");
        Assert.Equal(FileTransferState.Accepted, session.State);

        // 3. Authorized open transitions to Transferring
        var authorizedOpenTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "open-valid") authorizedOpenTcs.TrySetResult(iq);
        };

        var authorizedOpen = XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='open-valid'>" +
            "<open xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-3' block-size='4096'/>" +
            "</iq>");
        await feature.OnIncomingElementAsync(client, authorizedOpen);

        var openResp = await authorizedOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(FileTransferState.Transferring, session.State);
        Assert.True(started);
        Assert.True(openResp.IsResult);

        // 4. Mallory sending data is rejected with forbidden
        var malloryDataTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "data-mallory") malloryDataTcs.TrySetResult(iq);
        };

        var spoofedData = XmppElement.Parse(
            "<iq type='set' from='mallory@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='data-mallory'>" +
            "<data xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-3' seq='0'>AQIDBA==</data>" +
            "</iq>");
        await feature.OnIncomingElementAsync(client, spoofedData);

        var malloryDataResp = await malloryDataTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, session.BytesTransferred);
        Assert.True(malloryDataResp.IsError);
        Assert.NotNull(malloryDataResp.RawElement.Element("error")?.Element("forbidden", "urn:ietf:params:xml:ns:xmpp-stanzas"));

        // 5. Alice sending data succeeds
        var aliceDataTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "data-valid") aliceDataTcs.TrySetResult(iq);
        };

        var authorizedData = XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='data-valid'>" +
            "<data xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-3' seq='0'>AQIDBA==</data>" +
            "</iq>");
        await feature.OnIncomingElementAsync(client, authorizedData);

        var aliceDataResp = await aliceDataTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(4, session.BytesTransferred);
        Assert.True(aliceDataResp.IsResult);

        // 6. Mallory sending close is rejected with forbidden
        var malloryCloseTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "close-mallory") malloryCloseTcs.TrySetResult(iq);
        };

        var spoofedClose = XmppElement.Parse(
            "<iq type='set' from='mallory@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='close-mallory'>" +
            "<close xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-3'/>" +
            "</iq>");
        await feature.OnIncomingElementAsync(client, spoofedClose);

        var malloryCloseResp = await malloryCloseTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(malloryCloseResp.IsError);
        Assert.NotNull(malloryCloseResp.RawElement.Element("error")?.Element("forbidden", "urn:ietf:params:xml:ns:xmpp-stanzas"));

        // 7. Alice sending close succeeds
        var aliceCloseTcs = new TaskCompletionSource<IqStanza>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnIqReceived += iq =>
        {
            if (iq.Id == "close-valid") aliceCloseTcs.TrySetResult(iq);
        };

        var authorizedClose = XmppElement.Parse(
            "<iq type='set' from='alice@mock.example.com/desktop' to='bob@mock.example.com/laptop' id='close-valid'>" +
            "<close xmlns='http://jabber.org/protocol/ibb' sid='sid-ibb-gating-3'/>" +
            "</iq>");
        await feature.OnIncomingElementAsync(client, authorizedClose);

        var aliceCloseResp = await aliceCloseTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(aliceCloseResp.IsResult);

        await client.DisconnectAsync();
    }
}
