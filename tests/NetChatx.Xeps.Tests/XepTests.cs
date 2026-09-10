using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Transport;
using NetChatx.Core.Xml;
using NetChatx.MockServer;
using NetChatx.Protocol.Xeps.Core;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Protocol.Xeps.Muc;
using NetChatx.Protocol.Xeps.Omemo;
using NetChatx.Protocol.Xeps.Resilience;
using Xunit;

namespace NetChatx.Xeps.Tests;

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
}
