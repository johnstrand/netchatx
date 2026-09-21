using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Transport;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Core;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Protocol.Xeps.Muc;
using NetChatx.Protocol.Xeps.Omemo;
using NetChatx.Protocol.Xeps.Resilience;
using NetChatx.Protocol.Xeps.Sharing;
using Xunit;

namespace NetChatx.Xeps.Tests;

public class ResilienceAndCoreXepTests
{
    [Fact]
    public async Task Xep0198_StreamManagement_FullLifecycle_Works()
    {
        var sm = new Xep0198StreamManagement();
        Assert.Equal("urn:xmpp:sm:3", sm.FeatureUri);
        Assert.Equal("XEP-0198: Stream Management", sm.Name);
        Assert.False(sm.IsEnabled);

        // Cannot enable without client
        await Assert.ThrowsAsync<InvalidOperationException>(() => sm.EnableAsync());

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, transport);

        await sm.AttachAsync(client);

        // 1. Enable with resume
        await sm.EnableAsync(allowResume: true);

        // 2. Process enabled stanza
        var enabledElem = new XmppElement("enabled", Xep0198StreamManagement.NsSm)
            .Attr("id", "resume_token_999");
        var pass = await sm.OnIncomingElementAsync(client, enabledElem);
        Assert.False(pass); // Consumed by SM
        Assert.True(sm.IsEnabled);
        Assert.Equal("resume_token_999", sm.ResumeId);

        // 3. Track handled stanzas
        var msgIn = new XmppElement("message").Attr("from", "peer@example.com").Attr("to", "user@example.com");
        await sm.OnIncomingElementAsync(client, msgIn);
        Assert.Equal(1u, sm.InboundHandled);

        var msgOut = new XmppElement("message").Attr("to", "peer@example.com");
        await sm.OnOutgoingElementAsync(client, msgOut);
        Assert.Equal(1u, sm.OutboundHandled);

        // 4. Request ack and Send ack
        await sm.RequestAckAsync();
        await sm.SendAckAsync();

        // 5. Server requested ack <r/>
        var reqAck = new XmppElement("r", Xep0198StreamManagement.NsSm);
        pass = await sm.OnIncomingElementAsync(client, reqAck);
        Assert.False(pass);

        // 6. Server answered ack <a h='1'/>
        uint? receivedAck = null;
        sm.AckReceived += h => receivedAck = h;
        var ansAck = new XmppElement("a", Xep0198StreamManagement.NsSm).Attr("h", "1");
        pass = await sm.OnIncomingElementAsync(client, ansAck);
        Assert.False(pass);
        Assert.Equal(1u, receivedAck);

        // 7. Server failed SM <failed/>
        var failedElem = new XmppElement("failed", Xep0198StreamManagement.NsSm);
        pass = await sm.OnIncomingElementAsync(client, failedElem);
        Assert.False(pass);
        Assert.False(sm.IsEnabled);
    }

    [Fact]
    public async Task Xep0352_ClientStateIndication_SetActive_Works()
    {
        var csi = new Xep0352ClientStateIndication();
        Assert.Equal("urn:xmpp:csi:0", csi.FeatureUri);
        Assert.Equal("XEP-0352: Client State Indication", csi.Name);
        Assert.True(csi.IsActive);

        // Throws without client
        await Assert.ThrowsAsync<InvalidOperationException>(() => csi.SetActiveAsync(false));

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await csi.AttachAsync(client);

        await csi.SetActiveAsync(false);
        Assert.False(csi.IsActive);

        await csi.SetActiveAsync(true);
        Assert.True(csi.IsActive);
    }

    [Fact]
    public async Task Xep0199_Ping_HandlesIncomingPingAndNames()
    {
        var ping = new Xep0199Ping();
        Assert.Equal("urn:xmpp:ping", ping.FeatureUri);
        Assert.Equal("XEP-0199: XMPP Ping", ping.Name);

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await ping.AttachAsync(client);

        // Handle incoming ping
        var pingIq = new XmppElement("iq")
            .Attr("type", "get")
            .Attr("id", "ping_req_1")
            .Attr("from", "server.example.com")
            .Attr("to", "user@example.com")
            .Child(new XmppElement("ping", Xep0199Ping.NsPing));

        var handled = await ping.OnIncomingElementAsync(client, pingIq);
        Assert.False(handled); // Consumed and responded with pong

        // Other elements should pass through
        var otherIq = new XmppElement("iq").Attr("type", "get").Attr("id", "other_1");
        var passed = await ping.OnIncomingElementAsync(client, otherIq);
        Assert.True(passed);
    }

    [Fact]
    public async Task Xep0030_ServiceDiscovery_ItemsAndDiscoInfoQuery_Works()
    {
        var disco = new Xep0030ServiceDiscovery();
        Assert.Equal("http://jabber.org/protocol/disco#info", disco.FeatureUri);
        Assert.Equal("XEP-0030: Service Discovery", disco.Name);

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await disco.AttachAsync(client);

        // Responds to incoming disco#info query
        var queryIq = new XmppElement("iq")
            .Attr("type", "get")
            .Attr("id", "disco_get_1")
            .Attr("from", "peer@example.com")
            .Attr("to", "user@example.com")
            .Child(new XmppElement("query", Xep0030ServiceDiscovery.NsInfo));

        var handled = await disco.OnIncomingElementAsync(client, queryIq);
        Assert.False(handled); // Consumed by disco
    }

    [Fact]
    public void Xep0363_HttpFileUpload_SlotPropertiesAndValidation()
    {
        var upload = new Xep0363HttpFileUpload();
        Assert.Equal("urn:xmpp:http:upload:0", upload.FeatureUri);
        Assert.Equal("XEP-0363: HTTP File Upload", upload.Name);

        var slot = new HttpUploadSlot
        {
            PutUrl = "https://upload.example.com/put/123",
            GetUrl = "https://download.example.com/get/123"
        };
        slot.Headers["Authorization"] = "Bearer token";

        Assert.Equal("https://upload.example.com/put/123", slot.PutUrl);
        Assert.Equal("https://download.example.com/get/123", slot.GetUrl);
        Assert.True(slot.Headers.ContainsKey("authorization"));
        Assert.Equal("Bearer token", slot.Headers["authorization"]);
    }

    [Fact]
    public async Task Xep0045_MultiUserChat_FullLifecycle_Works()
    {
        var muc = new Xep0045MultiUserChat();
        Assert.Equal("http://jabber.org/protocol/muc", muc.FeatureUri);
        Assert.Equal("XEP-0045: Multi-User Chat", muc.Name);

        var roomJid = Jid.Parse("developers@conference.example.com");

        // Throws without client
        await Assert.ThrowsAsync<InvalidOperationException>(() => muc.JoinRoomAsync(roomJid, "Alice"));

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await muc.AttachAsync(client);

        // 1. Join room with password
        await muc.JoinRoomAsync(roomJid, "Alice", "secret");
        Assert.True(muc.JoinedRooms.ContainsKey(roomJid.BareJid));
        Assert.Equal("Alice", muc.JoinedRooms[roomJid.BareJid].Nickname);

        // 2. Track occupant presence available
        MucOccupant? changedOccupant = null;
        bool? isJoined = null;
        muc.RoomOccupantChanged += (r, occ, joined) =>
        {
            changedOccupant = occ;
            isJoined = joined;
        };

        var occPresence = new XmppElement("presence")
            .Attr("from", "developers@conference.example.com/Bob")
            .Attr("to", "alice@example.com")
            .Child(new XmppElement("x", Xep0045MultiUserChat.NsMucUser)
                .Child(new XmppElement("item")
                    .Attr("affiliation", "member")
                    .Attr("role", "participant")
                    .Attr("jid", "bob@example.com/laptop")))
            .Child(new XmppElement("status") { Value = "Online and coding" });

        var handled = await muc.OnIncomingElementAsync(client, occPresence);
        Assert.NotNull(changedOccupant);
        Assert.Equal("Bob", changedOccupant.Nickname);
        Assert.Equal("member", changedOccupant.Affiliation);
        Assert.Equal("participant", changedOccupant.Role);
        Assert.Equal("bob@example.com/laptop", changedOccupant.RealJid?.ToString());
        Assert.Equal("Online and coding", changedOccupant.Status);
        Assert.True(isJoined);

        // 3. Track subject change
        string? newSubject = null;
        muc.RoomSubjectChanged += (r, subj) => newSubject = subj;

        var subjMsg = new XmppElement("message")
            .Attr("type", "groupchat")
            .Attr("from", "developers@conference.example.com/Bob")
            .Attr("to", "alice@example.com")
            .Child(new XmppElement("subject") { Value = "Release 1.0 Planning" });

        await muc.OnIncomingElementAsync(client, subjMsg);
        Assert.Equal("Release 1.0 Planning", newSubject);
        Assert.Equal("Release 1.0 Planning", muc.JoinedRooms[roomJid.BareJid].Subject);

        // 4. Send group message and set subject
        await muc.SendGroupMessageAsync(roomJid, "Hello team!");
        await muc.SetSubjectAsync(roomJid, "New Subject");

        // 5. Track occupant presence unavailable
        var unavailPresence = new XmppElement("presence")
            .Attr("type", "unavailable")
            .Attr("from", "developers@conference.example.com/Bob")
            .Attr("to", "alice@example.com");

        await muc.OnIncomingElementAsync(client, unavailPresence);
        Assert.False(isJoined);

        // 6. Leave room
        await muc.LeaveRoomAsync(roomJid, "Gone for lunch");
        Assert.False(muc.JoinedRooms.ContainsKey(roomJid.BareJid));
    }

    [Fact]
    public void DoubleRatchetSession_EncryptionDecryptionExchange_Works()
    {
        var rootKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(rootKey);

        var aliceDh = OmemoCrypto.GenerateX25519KeyPair();
        var bobDh = OmemoCrypto.GenerateX25519KeyPair();

        var aliceSession = new DoubleRatchetSession(rootKey, aliceDh, bobDh.PublicKey, isInitiator: true);
        var bobSession = new DoubleRatchetSession(rootKey, bobDh, null, isInitiator: false);

        // Cannot encrypt if remote key is missing and no sending chain
        var bobNoKey = new DoubleRatchetSession(rootKey, bobDh, null, isInitiator: false);
        Assert.Throws<InvalidOperationException>(() => bobNoKey.RatchetEncrypt());

        // 1. Alice encrypts message 0
        var (aKey0, aIv0, aEphem0, aNum0) = aliceSession.RatchetEncrypt();
        Assert.Equal(0u, aNum0);

        // 2. Bob decrypts message 0
        var (bKey0, bIv0) = bobSession.RatchetDecrypt(aEphem0, aNum0);
        Assert.Equal(aKey0, bKey0);
        Assert.Equal(aIv0, bIv0);

        // 3. Alice encrypts message 1 with same DH ratchet step
        var (aKey1, aIv1, aEphem1, aNum1) = aliceSession.RatchetEncrypt();
        Assert.Equal(1u, aNum1);
        Assert.Equal(aEphem0, aEphem1);

        // Bob decrypts message 1
        var (bKey1, bIv1) = bobSession.RatchetDecrypt(aEphem1, aNum1);
        Assert.Equal(aKey1, bKey1);
        Assert.Equal(aIv1, bIv1);

        // 4. Bob replies (ratchet step)
        var (bSendKey, bSendIv, bEphem, bNum) = bobSession.RatchetEncrypt();
        Assert.Equal(0u, bNum);

        // Alice decrypts Bob's message
        var (aRecvKey, aRecvIv) = aliceSession.RatchetDecrypt(bEphem, bNum);
        Assert.Equal(bSendKey, aRecvKey);
        Assert.Equal(bSendIv, aRecvIv);
    }

    [Fact]
    public async Task Xep0085_ChatStates_SendReceiveAndAutoActive_Works()
    {
        var chatStates = new Xep0085ChatStates();
        Assert.Equal("http://jabber.org/protocol/chatstates", chatStates.FeatureUri);
        Assert.Equal("XEP-0085: Chat State Notifications", chatStates.Name);

        // Throws without client
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatStates.SendChatStateAsync(Jid.Parse("bob@example.com"), ChatState.Active));

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await chatStates.AttachAsync(client);

        // 1. Send each chat state
        var to = Jid.Parse("bob@example.com");
        await chatStates.SendChatStateAsync(to, ChatState.Composing);
        await chatStates.SendChatStateAsync(to, ChatState.Paused);
        await chatStates.SendChatStateAsync(to, ChatState.Inactive);
        await chatStates.SendChatStateAsync(to, ChatState.Gone);
        await chatStates.SendChatStateAsync(to, ChatState.Active);

        // 2. Incoming elements invoke event
        ChatState? receivedState = null;
        Jid? senderJid = null;
        chatStates.ChatStateReceived += (from, s) => { senderJid = from; receivedState = s; };

        foreach (var state in new[] { ChatState.Composing, ChatState.Paused, ChatState.Inactive, ChatState.Gone, ChatState.Active })
        {
            var stateName = state.ToString().ToLowerInvariant();
            var msg = new XmppElement("message")
                .Attr("from", "bob@example.com/phone")
                .Attr("to", "alice@example.com")
                .Child(new XmppElement(stateName, Xep0085ChatStates.NsChatStates));

            var pass = await chatStates.OnIncomingElementAsync(client, msg);
            Assert.True(pass);
            Assert.Equal(state, receivedState);
            Assert.Equal("bob@example.com", senderJid?.BareJid.ToString());
        }

        // 3. Outgoing auto-active added to message with body
        var outboundMsg = new XmppElement("message")
            .Attr("type", "chat")
            .Attr("to", "bob@example.com")
            .Child(new XmppElement("body") { Value = "Hello Bob" });

        await chatStates.OnOutgoingElementAsync(client, outboundMsg);
        var activeElem = outboundMsg.Element("active", Xep0085ChatStates.NsChatStates);
        Assert.NotNull(activeElem);
    }

    [Fact]
    public async Task Xep0184_MessageDeliveryReceipts_AutoAckAndRequest_Works()
    {
        var receipts = new Xep0184MessageDeliveryReceipts();
        Assert.Equal("urn:xmpp:receipts", receipts.FeatureUri);
        Assert.Equal("XEP-0184: Message Delivery Receipts", receipts.Name);

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await receipts.AttachAsync(client);

        // 1. Outgoing message attaches request
        var outboundMsg = new XmppElement("message")
            .Attr("type", "chat")
            .Attr("to", "bob@example.com");

        await receipts.OnOutgoingElementAsync(client, outboundMsg);
        Assert.NotNull(outboundMsg.Element("request", Xep0184MessageDeliveryReceipts.NsReceipts));

        // 2. Incoming message with request triggers auto-ack
        var inboundMsg = new XmppElement("message")
            .Attr("type", "chat")
            .Attr("from", "bob@example.com")
            .Attr("id", "msg_receipt_1")
            .Child(new XmppElement("request", Xep0184MessageDeliveryReceipts.NsReceipts));

        var pass = await receipts.OnIncomingElementAsync(client, inboundMsg);
        Assert.True(pass);

        // 3. Incoming receipt raises event
        string? ackedId = null;
        receipts.ReceiptReceived += (id, from) => ackedId = id;

        var receiptMsg = new XmppElement("message")
            .Attr("from", "bob@example.com")
            .Child(new XmppElement("received", Xep0184MessageDeliveryReceipts.NsReceipts).Attr("id", "msg_receipt_1"));

        await receipts.OnIncomingElementAsync(client, receiptMsg);
        Assert.Equal("msg_receipt_1", ackedId);
    }

    [Fact]
    public async Task Xep0308_LastMessageCorrection_SendReceive_Works()
    {
        var correction = new Xep0308LastMessageCorrection();
        Assert.Equal("urn:xmpp:message-correct:0", correction.FeatureUri);
        Assert.Equal("XEP-0308: Last Message Correction", correction.Name);

        // Throws without client
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            correction.SendCorrectionAsync(Jid.Parse("bob@example.com"), "orig_1", "corrected body"));

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await correction.AttachAsync(client);

        await correction.SendCorrectionAsync(Jid.Parse("bob@example.com"), "orig_1", "corrected body");

        // Incoming replace element raises event and is consumed
        string? correctedOrigId = null;
        correction.MessageCorrected += (msg, origId) => correctedOrigId = origId;

        var replaceElem = new XmppElement("message")
            .Attr("from", "bob@example.com")
            .Attr("to", "alice@example.com")
            .Child(new XmppElement("body") { Value = "corrected body" })
            .Child(new XmppElement("replace", Xep0308LastMessageCorrection.NsCorrection).Attr("id", "orig_1"));

        var handled = await correction.OnIncomingElementAsync(client, replaceElem);
        Assert.False(handled); // Consumed
        Assert.Equal("orig_1", correctedOrigId);
    }

    [Fact]
    public async Task Xep0424_MessageRetraction_SendReceive_Works()
    {
        var retraction = new Xep0424MessageRetraction();
        Assert.Equal("urn:xmpp:message-retract:1", retraction.FeatureUri);
        Assert.Equal("XEP-0424: Message Retraction", retraction.Name);

        var to = Jid.Parse("bob@example.com");
        var stanza = Xep0424MessageRetraction.CreateRetractionStanza(to, "target_99");
        Assert.Equal(Xep0424MessageRetraction.FallbackMessageText, stanza.Body);
        Assert.NotNull(stanza.RawElement.Element("retract", Xep0424MessageRetraction.NsRetraction1));

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@example.com"),
            Password = "pw"
        }, new LoopbackTransport());

        await retraction.AttachAsync(client);

        await retraction.SendRetractionAsync(to, "target_99");

        // Incoming retraction stanza raises event and is consumed
        string? retractedMsgId = null;
        retraction.MessageRetracted += (id, sender) => retractedMsgId = id;

        var handled = await retraction.OnIncomingElementAsync(client, stanza.RawElement);
        Assert.False(handled); // Consumed
        Assert.Equal("target_99", retractedMsgId);
    }
}

