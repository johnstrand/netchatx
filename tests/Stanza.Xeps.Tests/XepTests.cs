using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Core;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Protocol.Xeps.Muc;
using Stanza.Protocol.Xeps.Omemo;
using Stanza.Protocol.Xeps.Avatars;
using Stanza.Protocol.Xeps.Resilience;
using Xunit;

namespace Stanza.Xeps.Tests;

public class XepTests
{
    [Fact]
    public async Task Xep0444_Reactions_ParsingAndBuilding_Succeeds()
    {
        var reactions = new Xep0444Reactions();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());

        await reactions.AttachAsync(client);

        ReactionEventArgs? receivedArgs = null;
        reactions.ReactionReceived += args => receivedArgs = args;

        // Simulate incoming reaction stanza per XEP-0444
        var reactionStanza = new XmppElement("message")
            .Attr("from", "bob@mock.example.com/res")
            .Attr("to", "alice@mock.example.com")
            .Attr("type", "chat")
            .Child(new XmppElement("reactions", "urn:xmpp:reactions:0")
                .Attr("id", "msg123")
                .Child(new XmppElement("reaction") { Value = "👍" })
                .Child(new XmppElement("reaction") { Value = "🎉" }));

        await reactions.OnIncomingElementAsync(client, reactionStanza);

        Assert.NotNull(receivedArgs);
        Assert.Equal("msg123", receivedArgs.TargetMessageId);
        Assert.Equal("bob@mock.example.com/res", receivedArgs.SenderJid.ToString());
        Assert.Equal("bob@mock.example.com", receivedArgs.RemoteJid.ToString());
        Assert.Equal(2, receivedArgs.Emojis.Count);
        Assert.Contains("👍", receivedArgs.Emojis);
        Assert.Contains("🎉", receivedArgs.Emojis);
        Assert.False(receivedArgs.IsCarbonSent);
    }

    [Fact]
    public async Task Xep0444_Reactions_InsideCarbons_Succeeds()
    {
        var reactions = new Xep0444Reactions();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());

        await reactions.AttachAsync(client);

        ReactionEventArgs? receivedArgs = null;
        reactions.ReactionReceived += args => receivedArgs = args;

        // 1. Sent Carbon: Alice reacted on her phone to Bob's message
        var sentCarbonStanza = new XmppElement("message")
            .Attr("from", "alice@mock.example.com/phone")
            .Attr("to", "alice@mock.example.com/desktop")
            .Child(new XmppElement("sent", "urn:xmpp:carbons:2")
                .Child(new XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new XmppElement("message")
                        .Attr("from", "alice@mock.example.com/phone")
                        .Attr("to", "bob@mock.example.com")
                        .Attr("type", "chat")
                        .Child(new XmppElement("reactions", "urn:xmpp:reactions:0")
                            .Attr("id", "msg_sent_carbon_1")
                            .Child(new XmppElement("reaction") { Value = "❤️" })))));

        await reactions.OnIncomingElementAsync(client, sentCarbonStanza);

        Assert.NotNull(receivedArgs);
        Assert.Equal("msg_sent_carbon_1", receivedArgs.TargetMessageId);
        Assert.Equal("bob@mock.example.com", receivedArgs.RemoteJid.ToString());
        Assert.True(receivedArgs.IsCarbonSent);
        Assert.Single(receivedArgs.Emojis);
        Assert.Equal("❤️", receivedArgs.Emojis[0]);

        // 2. Received Carbon: Bob reacted to Alice, carbon-copied to desktop
        receivedArgs = null;
        var receivedCarbonStanza = new XmppElement("message")
            .Attr("from", "alice@mock.example.com/phone")
            .Attr("to", "alice@mock.example.com/desktop")
            .Child(new XmppElement("received", "urn:xmpp:carbons:2")
                .Child(new XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new XmppElement("message")
                        .Attr("from", "bob@mock.example.com/mobile")
                        .Attr("to", "alice@mock.example.com/phone")
                        .Attr("type", "chat")
                        .Child(new XmppElement("reactions", "urn:xmpp:reactions:0")
                            .Attr("id", "msg_recv_carbon_2")
                            .Child(new XmppElement("reaction") { Value = "🔥" })))));

        await reactions.OnIncomingElementAsync(client, receivedCarbonStanza);

        Assert.NotNull(receivedArgs);
        Assert.Equal("msg_recv_carbon_2", receivedArgs.TargetMessageId);
        Assert.Equal("bob@mock.example.com", receivedArgs.RemoteJid.ToString());
        Assert.False(receivedArgs.IsCarbonSent);
        Assert.Single(receivedArgs.Emojis);
        Assert.Equal("🔥", receivedArgs.Emojis[0]);
    }

    [Fact]
    public async Task Xep0030_And_Xep0199_PingAndDisco_Succeeds()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, transport);

        var disco = new Xep0030ServiceDiscovery();
        var ping = new Xep0199Ping();

        await disco.AttachAsync(client);
        await ping.AttachAsync(client);

        await client.ConnectAsync();

        // Test Ping
        var latency = await ping.PingAsync(Jid.Parse("mock.example.com"));
        Assert.True(latency >= TimeSpan.Zero);

        // Test Disco
        var discoResult = await disco.DiscoverInfoAsync(Jid.Parse("mock.example.com"));
        Assert.Contains("urn:xmpp:ping", discoResult.Features);
        Assert.Contains("urn:xmpp:carbons:2", discoResult.Features);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Xep0198_StreamManagement_TracksStanzasAndAcks()
    {
        var sm = new Xep0198StreamManagement();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());

        await sm.AttachAsync(client);

        // Simulate enabled element
        var enabledElem = new XmppElement("enabled", "urn:xmpp:sm:3")
            .Attr("id", "resume-token-123")
            .Attr("resume", "true");

        await sm.OnIncomingElementAsync(client, enabledElem);
        Assert.True(sm.IsEnabled);
        Assert.Equal("resume-token-123", sm.ResumeId);

        // Simulate inbound stanzas
        await sm.OnIncomingElementAsync(client, new XmppElement("message"));
        await sm.OnIncomingElementAsync(client, new XmppElement("presence"));
        await sm.OnIncomingElementAsync(client, new XmppElement("iq"));

        Assert.Equal(3u, sm.InboundHandled);
    }

    [Fact]
    public async Task Xep0280_MessageCarbons_DispatchesReceivedCarbon()
    {
        var carbons = new Xep0280MessageCarbons();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());

        await carbons.AttachAsync(client);

        MessageStanza? dispatchedMsg = null;
        bool? wasSentByUs = null;

        carbons.CarbonMessageReceived += (msg, isSent) =>
        {
            dispatchedMsg = msg;
            wasSentByUs = isSent;
        };

        var carbonStanza = new XmppElement("message")
            .Attr("from", "alice@mock.example.com")
            .Attr("to", "alice@mock.example.com/other-device")
            .Child(new XmppElement("received", "urn:xmpp:carbons:2")
                .Child(new XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new XmppElement("message")
                        .Attr("from", "bob@mock.example.com")
                        .Attr("to", "alice@mock.example.com")
                        .Child(new XmppElement("body") { Value = "Carbon message from Bob" }))));

        await carbons.OnIncomingElementAsync(client, carbonStanza);

        Assert.NotNull(dispatchedMsg);
        Assert.Equal("Carbon message from Bob", dispatchedMsg.Body);
        Assert.False(wasSentByUs);
    }

    [Fact]
    public async Task Xep0045_MultiUserChat_TracksOccupants()
    {
        var muc = new Xep0045MultiUserChat();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());

        await muc.AttachAsync(client);

        var roomJid = Jid.Parse("room@conference.mock.example.com");

        // Simulate joining
        await muc.JoinRoomAsync(roomJid, "Alice");
        Assert.True(muc.JoinedRooms.ContainsKey(roomJid));

        // Simulate presence from another occupant Bob
        var bobPresence = new XmppElement("presence")
            .Attr("from", "room@conference.mock.example.com/Bob")
            .Child(new XmppElement("x", "http://jabber.org/protocol/muc#user")
                .Child(new XmppElement("item")
                    .Attr("affiliation", "member")
                    .Attr("role", "participant")
                    .Attr("jid", "bob@mock.example.com")))
            .Child(new XmppElement("status") { Value = "Ready to chat" });

        await muc.OnIncomingElementAsync(client, bobPresence);

        var room = muc.JoinedRooms[roomJid];
        Assert.True(room.Occupants.ContainsKey("Bob"));
        var bob = room.Occupants["Bob"];
        Assert.Equal("member", bob.Affiliation);
        Assert.Equal("participant", bob.Role);
        Assert.Equal("bob@mock.example.com", bob.RealJid?.ToString());
        Assert.Equal("Ready to chat", bob.Status);
    }

    [Fact]
    public async Task Xep0384_Omemo_EncryptDecryptRoundtrip_Succeeds()
    {
        // Setup Alice OMEMO manager
        var aliceOmemo = new Xep0384OmemoManager();
        var aliceClient = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());
        await aliceOmemo.AttachAsync(aliceClient);

        // Setup Bob OMEMO manager
        var bobOmemo = new Xep0384OmemoManager();
        var bobClient = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("bob@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());
        await bobOmemo.AttachAsync(bobClient);

        DecryptedOmemoMessage? bobReceived = null;
        bobOmemo.MessageDecrypted += msg => bobReceived = msg;

        // Alice encrypts for Bob
        var encryptedStanza = await aliceOmemo.EncryptMessageAsync(
            to: Jid.Parse("bob@mock.example.com"),
            plaintext: "Top Secret OMEMO Message",
            remoteDeviceId: bobOmemo.LocalDeviceId,
            remotePublicKey: bobOmemo.IdentityKey.PublicKey);

        // Verify XML envelope structure
        Assert.NotNull(encryptedStanza.RawElement.Element("encrypted", "urn:xmpp:omemo:2"));

        // Bob receives and decrypts
        encryptedStanza.RawElement.Attr("from", "alice@mock.example.com/res");
        await bobOmemo.OnIncomingElementAsync(bobClient, encryptedStanza.RawElement);

        Assert.NotNull(bobReceived);
        Assert.Equal("Top Secret OMEMO Message", bobReceived.PlaintextBody);
        Assert.Equal(aliceOmemo.LocalDeviceId, bobReceived.SenderDeviceId);
    }

    [Fact]
    public async Task Xep0313_MAM_QueryAndParsing_WorksCorrectly()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, transport);

        var mam = new Xep0313MessageArchiveManagement();
        await mam.AttachAsync(client);
        await client.ConnectAsync();

        IqStanza? capturedQueryIq = null;
        server.OnIqReceived += iq =>
        {
            if (iq.RawElement.Element("query", "urn:xmpp:mam:2") is not null)
            {
                capturedQueryIq = iq;
            }
        };

        // 1. Query with full JID and 'end' timestamp (backward paging)
        var endTimestamp = new DateTimeOffset(2026, 8, 12, 14, 30, 0, TimeSpan.Zero);
        var queryTask = mam.QueryArchiveAsync(
            withJid: Jid.Parse("bob@mock.example.com/mobile"),
            maxResults: 30,
            end: endTimestamp);

        // Inject MAM result message while query is in-flight
        var mamResultMsg = new XmppElement("message")
            .Child(new XmppElement("result", "urn:xmpp:mam:2")
                .Attr("id", "mam_arch_42")
                .Child(new XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new XmppElement("delay", "urn:xmpp:delay")
                        .Attr("stamp", "2026-08-12T14:29:50Z"))
                    .Child(new XmppElement("message")
                        .Attr("from", "bob@mock.example.com")
                        .Attr("to", "alice@mock.example.com")
                        .Child(new XmppElement("body") { Value = "Historical MAM message" }))));

        await server.InjectElementAsync(mamResultMsg);

        var result = await queryTask;

        Assert.NotNull(capturedQueryIq);
        var queryElem = capturedQueryIq.RawElement.Element("query", "urn:xmpp:mam:2");
        Assert.NotNull(queryElem);

        // Verify with filter uses bare JID
        var form = queryElem.Element("x", "jabber:x:data");
        Assert.NotNull(form);
        var withField = form.Elements("field").FirstOrDefault(f => f.GetAttr("var") == "with");
        Assert.NotNull(withField);
        Assert.Equal("bob@mock.example.com", withField.Element("value")?.Value);

        // Verify end field
        var endField = form.Elements("field").FirstOrDefault(f => f.GetAttr("var") == "end");
        Assert.NotNull(endField);
        Assert.Equal("2026-08-12T14:30:00Z", endField.Element("value")?.Value);

        // Verify RSM before element is present for backward paging
        var rsm = queryElem.Element("set", "http://jabber.org/protocol/rsm");
        Assert.NotNull(rsm);
        Assert.NotNull(rsm.Element("before"));

        // Verify parsed result item
        Assert.Single(result.Messages);
        var item = result.Messages[0];
        Assert.Equal("mam_arch_42", item.ArchiveId);
        Assert.Equal("Historical MAM message", item.Message.Body);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 14, 29, 50, TimeSpan.Zero), item.Timestamp);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Xep0313_MUC_QueryArchive_UsesRoomJidAsTo()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com/desktop"),
            Password = "password123"
        }, transport);

        var mam = new Xep0313MessageArchiveManagement();
        await mam.AttachAsync(client);
        await client.ConnectAsync();

        IqStanza? capturedQueryIq = null;
        server.OnIqReceived += iq =>
        {
            if (iq.RawElement.Element("query", "urn:xmpp:mam:2") is not null)
            {
                capturedQueryIq = iq;
            }
        };

        var roomJid = Jid.Parse("developers@conference.mock.example.com");
        var queryTask = mam.QueryArchiveAsync(archiveJid: roomJid, maxResults: 25);

        var result = await queryTask;

        Assert.NotNull(capturedQueryIq);
        Assert.Equal(roomJid, capturedQueryIq.To);
        var queryElem = capturedQueryIq.RawElement.Element("query", "urn:xmpp:mam:2");
        Assert.NotNull(queryElem);

        // In MUC MAM, 'with' should not be present
        var form = queryElem.Element("x", "jabber:x:data");
        Assert.NotNull(form);
        var withField = form.Elements("field").FirstOrDefault(f => f.GetAttr("var") == "with");
        Assert.Null(withField);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Xep0333_ChatMarkers_AttachesMarkableAndDispatchesMarker()
    {
        var chatMarkers = new Xep0333ChatMarkers();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());

        await chatMarkers.AttachAsync(client);

        // 1. Outgoing message should have <markable/> added automatically
        var outgoingMsg = new XmppElement("message")
            .Attr("type", "chat")
            .Child(new XmppElement("body") { Value = "Hello!" });

        await chatMarkers.OnOutgoingElementAsync(client, outgoingMsg);
        Assert.NotNull(outgoingMsg.Element("markable", Xep0333ChatMarkers.NsChatMarkers));

        // 2. Incoming <displayed/> element should trigger MarkerReceived event
        string? receivedId = null;
        Jid? receivedFrom = null;
        ChatMarkerType? receivedType = null;

        chatMarkers.MarkerReceived += (id, fromJid, type) =>
        {
            receivedId = id;
            receivedFrom = fromJid;
            receivedType = type;
        };

        var incomingDisplayed = new XmppElement("message")
            .Attr("from", "bob@mock.example.com/mobile")
            .Child(new XmppElement("displayed", Xep0333ChatMarkers.NsChatMarkers).Attr("id", "msg_12345"));

        await chatMarkers.OnIncomingElementAsync(client, incomingDisplayed);

        Assert.Equal("msg_12345", receivedId);
        Assert.Equal("bob@mock.example.com/mobile", receivedFrom?.ToString());
        Assert.Equal(ChatMarkerType.Displayed, receivedType);
    }

    [Fact]
    public async Task Xep0085_ChatStates_SendAndReceive_WorksCorrectly()
    {
        var chatStates = new Xep0085ChatStates();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());

        await chatStates.AttachAsync(client);

        Jid? receivedFrom = null;
        ChatState? receivedState = null;

        chatStates.ChatStateReceived += (fromJid, state) =>
        {
            receivedFrom = fromJid;
            receivedState = state;
        };

        var incomingComposing = new XmppElement("message")
            .Attr("from", "bob@mock.example.com/mobile")
            .Child(new XmppElement("composing", Xep0085ChatStates.NsChatStates));

        await chatStates.OnIncomingElementAsync(client, incomingComposing);

        Assert.Equal("bob@mock.example.com/mobile", receivedFrom?.ToString());
        Assert.Equal(ChatState.Composing, receivedState);
    }

    [Fact]
    public async Task Xep0308_LastMessageCorrection_SendAndReceive_WorksCorrectly()
    {
        var correction = new Xep0308LastMessageCorrection();
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transport);

        await correction.AttachAsync(client);
        await client.ConnectAsync();

        MessageStanza? serverReceivedStanza = null;
        server.OnMessageReceived += msg => serverReceivedStanza = msg;

        // 1. Send correction test
        await correction.SendCorrectionAsync(Jid.Parse("bob@mock.example.com"), "orig_msg_1", "Edited message content");

        for (int i = 0; i < 20 && serverReceivedStanza is null; i++) await Task.Delay(25);

        Assert.NotNull(serverReceivedStanza);
        Assert.Equal("Edited message content", serverReceivedStanza.Body);
        var replaceElem = serverReceivedStanza.RawElement.Element("replace", Xep0308LastMessageCorrection.NsCorrection);
        Assert.NotNull(replaceElem);
        Assert.Equal("orig_msg_1", replaceElem.GetAttr("id"));

        // 2. Receive incoming correction test
        MessageStanza? clientReceivedStanza = null;
        string? clientReceivedOriginalId = null;

        correction.MessageCorrected += (msg, originalId) =>
        {
            clientReceivedStanza = msg;
            clientReceivedOriginalId = originalId;
        };

        var incomingCorrection = new XmppElement("message")
            .Attr("from", "bob@mock.example.com/mobile")
            .Attr("to", "alice@mock.example.com")
            .Child(new XmppElement("body") { Value = "New corrected body" })
            .Child(new XmppElement("replace", Xep0308LastMessageCorrection.NsCorrection).Attr("id", "msg_to_replace_42"));

        var pass = await correction.OnIncomingElementAsync(client, incomingCorrection);
        Assert.False(pass); // Consumed by filter
        Assert.NotNull(clientReceivedStanza);
        Assert.Equal("New corrected body", clientReceivedStanza.Body);
        Assert.Equal("msg_to_replace_42", clientReceivedOriginalId);
    }

    [Fact]
    public async Task Xep0424_MessageRetraction_SendAndReceive_WorksCorrectly()
    {
        var retraction = new Xep0424MessageRetraction();
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        }, transport);

        await retraction.AttachAsync(client);
        await client.ConnectAsync();

        MessageStanza? serverReceivedStanza = null;
        server.OnMessageReceived += msg => serverReceivedStanza = msg;

        // 1. Send retraction test
        await retraction.SendRetractionAsync(Jid.Parse("bob@mock.example.com"), "msg_to_delete_99");

        for (int i = 0; i < 20 && serverReceivedStanza is null; i++) await Task.Delay(25);

        Assert.NotNull(serverReceivedStanza);
        Assert.Equal("bob@mock.example.com", serverReceivedStanza.To?.ToString());
        Assert.Equal("chat", serverReceivedStanza.Type);
        Assert.Equal("jabber:client", serverReceivedStanza.RawElement.GetAttr("xmlns"));

        var originIdElem = serverReceivedStanza.RawElement.Element("origin-id", "urn:xmpp:sid:0");
        Assert.NotNull(originIdElem);
        Assert.Equal(serverReceivedStanza.Id, originIdElem.GetAttr("id"));

        var retractElem = serverReceivedStanza.RawElement.Element("retract", Xep0424MessageRetraction.NsRetraction1);
        Assert.NotNull(retractElem);
        Assert.Equal("msg_to_delete_99", retractElem.GetAttr("id"));

        var fallbackElem = serverReceivedStanza.RawElement.Element("fallback", Xep0424MessageRetraction.NsFallback);
        Assert.NotNull(fallbackElem);
        Assert.Equal(Xep0424MessageRetraction.NsRetraction1, fallbackElem.GetAttr("for"));

        Assert.Equal(Xep0424MessageRetraction.FallbackMessageText, serverReceivedStanza.Body);

        // 2. Receive incoming retraction test (retract:1)
        string? retractedTargetId = null;
        Jid? retractedSenderJid = null;

        retraction.MessageRetracted += (targetId, sender) =>
        {
            retractedTargetId = targetId;
            retractedSenderJid = sender;
        };

        var incomingRetraction = new XmppElement("message")
            .Attr("from", "bob@mock.example.com/mobile")
            .Attr("to", "alice@mock.example.com")
            .Child(new XmppElement("retract", Xep0424MessageRetraction.NsRetraction1).Attr("id", "target_retract_55"));

        var pass = await retraction.OnIncomingElementAsync(client, incomingRetraction);
        Assert.False(pass); // Consumed by filter
        Assert.Equal("target_retract_55", retractedTargetId);
        Assert.Equal("bob@mock.example.com/mobile", retractedSenderJid?.ToString());
    }

    [Fact]
    public void Xep0424_CreateRetractionStanza_MatchesSpecification()
    {
        var to = Jid.Parse("richard@squishythoughts.com");
        var msg = Xep0424MessageRetraction.CreateRetractionStanza(to, "3ecb8966023d449db0d20114071be2c9");
        msg.Id = "06c46d61-65dc-4724-a994-55ee57fea07b";
        msg.RawElement.Element("origin-id", "urn:xmpp:sid:0")!.Attr("id", msg.Id);

        var xml = msg.ToXmlString(indent: true);
        var parsed = XmppElement.Parse(xml);

        Assert.Equal("richard@squishythoughts.com", parsed.GetAttr("to"));
        Assert.Equal("chat", parsed.GetAttr("type"));
        Assert.Equal("06c46d61-65dc-4724-a994-55ee57fea07b", parsed.GetAttr("id"));
        Assert.Equal("jabber:client", parsed.GetAttr("xmlns"));

        var originId = parsed.Element("origin-id", "urn:xmpp:sid:0");
        Assert.NotNull(originId);
        Assert.Equal("06c46d61-65dc-4724-a994-55ee57fea07b", originId.GetAttr("id"));

        var retract = parsed.Element("retract", Xep0424MessageRetraction.NsRetraction1);
        Assert.NotNull(retract);
        Assert.Equal("3ecb8966023d449db0d20114071be2c9", retract.GetAttr("id"));

        var fallback = parsed.Element("fallback", Xep0424MessageRetraction.NsFallback);
        Assert.NotNull(fallback);
        Assert.Equal(Xep0424MessageRetraction.NsRetraction1, fallback.GetAttr("for"));

        Assert.Equal(Xep0424MessageRetraction.FallbackMessageText, parsed.Element("body")?.Value);
    }

    [Fact]
    public void Xep0393_MessageStyling_DiscoveryAndHelpers_Work()
    {
        var styling = new Xep0393MessageStyling();
        Assert.Equal("urn:xmpp:styling:0", styling.FeatureUri);
        Assert.Equal("XEP-0393: Message Styling", styling.Name);

        var elem = new XmppElement("message");
        Assert.False(Xep0393MessageStyling.IsUnstyled(elem));

        Xep0393MessageStyling.AttachUnstyled(elem);
        Assert.True(Xep0393MessageStyling.IsUnstyled(elem));
        Assert.NotNull(elem.Element("unstyled", "urn:xmpp:styling:0"));
    }

    [Fact]
    public async Task Xep0084_AvatarMetadataReceived_FiresCorrectly()
    {
        var xep = new Xep0084UserAvatar();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());
        await xep.AttachAsync(client);

        Jid? updatedJid = null;
        AvatarMetadata? receivedMeta = null;
        xep.AvatarMetadataReceived += (j, m) =>
        {
            updatedJid = j;
            receivedMeta = m;
        };

        var eventMsg = new XmppElement("message")
            .Attr("from", "bob@mock.example.com")
            .Attr("to", "alice@mock.example.com")
            .Child(new XmppElement("event", "http://jabber.org/protocol/pubsub#event")
                .Child(new XmppElement("items")
                    .Attr("node", "urn:xmpp:avatar:metadata")
                    .Child(new XmppElement("item")
                        .Attr("id", "111f4b3c50d7b0df729d299bc6f8e817b0e37c50")
                        .Child(new XmppElement("metadata", "urn:xmpp:avatar:metadata")
                            .Child(new XmppElement("info")
                                .Attr("id", "111f4b3c50d7b0df729d299bc6f8e817b0e37c50")
                                .Attr("type", "image/png")
                                .Attr("bytes", "12345")
                                .Attr("width", "64")
                                .Attr("height", "64"))))));

        await xep.OnIncomingElementAsync(client, eventMsg);

        Assert.NotNull(updatedJid);
        Assert.Equal("bob@mock.example.com", updatedJid.ToBareString());
        Assert.NotNull(receivedMeta);
        Assert.Equal("111f4b3c50d7b0df729d299bc6f8e817b0e37c50", receivedMeta.Id);
        Assert.Equal("image/png", receivedMeta.MimeType);
        Assert.Equal(12345, receivedMeta.Bytes);
        Assert.Equal(64, receivedMeta.Width);
        Assert.Equal(64, receivedMeta.Height);
    }

    [Fact]
    public async Task Xep0084_AvatarMetadataCleared_FiresWhenEmptyMetadata()
    {
        var xep = new Xep0084UserAvatar();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());
        await xep.AttachAsync(client);

        Jid? clearedJid = null;
        xep.AvatarMetadataCleared += j => clearedJid = j;

        var clearMsg = new XmppElement("message")
            .Attr("from", "bob@mock.example.com")
            .Attr("to", "alice@mock.example.com")
            .Child(new XmppElement("event", "http://jabber.org/protocol/pubsub#event")
                .Child(new XmppElement("items")
                    .Attr("node", "urn:xmpp:avatar:metadata")
                    .Child(new XmppElement("item")
                        .Attr("id", "clear-item")
                        .Child(new XmppElement("metadata", "urn:xmpp:avatar:metadata")))));

        await xep.OnIncomingElementAsync(client, clearMsg);

        Assert.NotNull(clearedJid);
        Assert.Equal("bob@mock.example.com", clearedJid.ToBareString());
    }

    [Fact]
    public async Task Xep0153_AvatarHashReceived_FiresFromPresence()
    {
        var xep = new Xep0153VCardAvatar();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());
        await xep.AttachAsync(client);

        Jid? senderJid = null;
        string? receivedHash = null;
        xep.AvatarHashReceived += (j, h) =>
        {
            senderJid = j;
            receivedHash = h;
        };

        var pres = new XmppElement("presence")
            .Attr("from", "bob@mock.example.com/phone")
            .Attr("to", "alice@mock.example.com")
            .Child(new XmppElement("x", "vcard-temp:x:update")
                .Child(new XmppElement("photo") { Value = "abc123hash" }));

        await xep.OnIncomingElementAsync(client, pres);

        Assert.NotNull(senderJid);
        Assert.Equal("bob@mock.example.com", senderJid.ToBareString());
        Assert.Equal("abc123hash", receivedHash);
    }

    [Fact]
    public async Task Xep0153_OutgoingPresence_InjectsAvatarHash()
    {
        var xep = new Xep0153VCardAvatar();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());
        await xep.AttachAsync(client);

        xep.CurrentAvatarHash = "current_sha1_hash";

        var pres = new XmppElement("presence");
        await xep.OnOutgoingElementAsync(client, pres);

        var updateX = pres.Element("x", "vcard-temp:x:update");
        Assert.NotNull(updateX);
        var photo = updateX.Element("photo");
        Assert.NotNull(photo);
        Assert.Equal("current_sha1_hash", photo.Value);
    }

    [Fact]
    public async Task AvatarManager_Coordination_Works()
    {
        var manager = new AvatarManager();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, new LoopbackTransport());
        await manager.AttachAsync(client);

        AvatarChangedEventArgs? args = null;
        manager.AvatarUpdated += a => args = a;

        // Simulate incoming presence update
        var pres = new XmppElement("presence")
            .Attr("from", "carol@mock.example.com/desktop")
            .Child(new XmppElement("x", "vcard-temp:x:update")
                .Child(new XmppElement("photo") { Value = "sha1_of_carol" }));

        await manager.VCardAvatarXep.OnIncomingElementAsync(client, pres);

        Assert.NotNull(args);
        Assert.Equal("carol@mock.example.com", args.Jid.ToBareString());
        Assert.Equal("sha1_of_carol", args.Hash);
        Assert.False(args.IsCleared);
        Assert.Equal("XEP-0153", args.Source);

        var dummyData = "Hello Avatar"u8.ToArray();
        var sha1 = AvatarManager.ComputeSha1(dummyData);
        Assert.NotEmpty(sha1);
        Assert.Equal(40, sha1.Length);
    }
}

