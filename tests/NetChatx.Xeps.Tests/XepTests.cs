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
}
