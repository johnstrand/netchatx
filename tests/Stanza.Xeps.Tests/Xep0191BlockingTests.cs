using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Privacy;
using Xunit;

namespace Stanza.Xeps.Tests;

public class Xep0191BlockingTests
{
    [Fact]
    public void FeatureMetadata_IsCorrect()
    {
        var blocking = new Xep0191Blocking();
        Assert.Equal("XEP-0191: Blocking Command", blocking.Name);
        Assert.Equal("urn:xmpp:blocking", blocking.FeatureUri);
        Assert.Empty(blocking.BlockedJids);
    }

    [Fact]
    public async Task IsBlocked_MatchesBareFullAndDomainCaseInsensitively()
    {
        var blocking = new Xep0191Blocking();

        // Simulate incoming push with items
        var pushElem = new XmppElement("iq")
            .Attr("type", "set")
            .Attr("id", "push1")
            .Child(new XmppElement("block", Xep0191Blocking.NsBlocking)
                .Child(new XmppElement("item").Attr("jid", "spammer@bad.org"))
                .Child(new XmppElement("item").Attr("jid", "evil.com"))
                .Child(new XmppElement("item").Attr("jid", "stalker@example.com/mobile")));

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("me@example.com/desktop"),
            Password = "pass"
        }, transport);

        await blocking.AttachAsync(client);

        var handled = await blocking.OnIncomingElementAsync(client, pushElem);
        Assert.False(handled); // Handled by blocking XEP

        // Check bare JID match
        Assert.True(blocking.IsBlocked("spammer@bad.org"));
        Assert.True(blocking.IsBlocked("SPAMMER@bad.org/any-resource"));
        Assert.True(blocking.IsBlocked(Jid.Parse("spammer@bad.org/resource2")));

        // Check domain match
        Assert.True(blocking.IsBlocked("anybody@evil.com"));
        Assert.True(blocking.IsBlocked("evil.com"));
        Assert.True(blocking.IsBlocked(Jid.Parse("someone@evil.com/res")));

        // Check full JID match
        Assert.True(blocking.IsBlocked("stalker@example.com/mobile"));
        Assert.False(blocking.IsBlocked("stalker@example.com/desktop"));

        // Check unblocked user
        Assert.False(blocking.IsBlocked("friend@example.com"));
    }

    [Fact]
    public async Task GetBlockList_Block_Unblock_Lifecycle_WithMockServer()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com/stanza"),
            Password = "password123"
        }, transport);

        var blocking = new Xep0191Blocking();
        await blocking.AttachAsync(client);

        IReadOnlyList<string>? updatedList = null;
        IReadOnlyList<string>? blockedEvents = null;
        IReadOnlyList<string>? unblockedEvents = null;

        blocking.BlockListUpdated += list => updatedList = list;
        blocking.ContactsBlocked += list => blockedEvents = list;
        blocking.ContactsUnblocked += list => unblockedEvents = list;

        await client.ConnectAsync();

        // 1. Initial block list should be empty
        var initial = await blocking.GetBlockListAsync();
        Assert.Empty(initial);
        Assert.NotNull(updatedList);
        Assert.Empty(updatedList);

        // 2. Block 2 contacts
        await blocking.BlockAsync(["baduser1@mock.example.com", "baduser2@mock.example.com"]);
        Assert.Equal(2, blocking.BlockedJids.Count);
        Assert.True(blocking.IsBlocked("baduser1@mock.example.com"));
        Assert.True(blocking.IsBlocked("baduser2@mock.example.com"));
        Assert.NotNull(blockedEvents);
        Assert.Equal(2, blockedEvents.Count);

        // 3. Unblock 1 contact
        await blocking.UnblockAsync("baduser1@mock.example.com");
        Assert.Single(blocking.BlockedJids);
        Assert.False(blocking.IsBlocked("baduser1@mock.example.com"));
        Assert.True(blocking.IsBlocked("baduser2@mock.example.com"));
        Assert.NotNull(unblockedEvents);
        Assert.Contains("baduser1@mock.example.com", unblockedEvents);

        // 4. Unblock All
        bool clearedFired = false;
        blocking.BlockListCleared += () => clearedFired = true;
        await blocking.UnblockAllAsync();
        Assert.Empty(blocking.BlockedJids);
        Assert.False(blocking.IsBlocked("baduser2@mock.example.com"));
        Assert.True(clearedFired);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task InboundServerPush_BlockAndUnblock_UpdatesLocalStateAndSendsResult()
    {
        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@example.com/desktop"),
            Password = "pass"
        }, transport);

        var blocking = new Xep0191Blocking();
        await blocking.AttachAsync(client);

        var blockedEventList = new List<string>();
        blocking.ContactsBlocked += items => blockedEventList.AddRange(items);

        var unblockedEventList = new List<string>();
        blocking.ContactsUnblocked += items => unblockedEventList.AddRange(items);

        // Server push: block user
        var blockPush = new XmppElement("iq")
            .Attr("type", "set")
            .Attr("id", "push-block-1")
            .Child(new XmppElement("block", Xep0191Blocking.NsBlocking)
                .Child(new XmppElement("item").Attr("jid", "spammer@example.com")));

        var handledBlock = await blocking.OnIncomingElementAsync(client, blockPush);
        Assert.False(handledBlock); // consumed
        Assert.True(blocking.IsBlocked("spammer@example.com"));
        Assert.Contains("spammer@example.com", blockedEventList);

        // Server push: unblock user
        var unblockPush = new XmppElement("iq")
            .Attr("type", "set")
            .Attr("id", "push-unblock-1")
            .Child(new XmppElement("unblock", Xep0191Blocking.NsBlocking)
                .Child(new XmppElement("item").Attr("jid", "spammer@example.com")));

        var handledUnblock = await blocking.OnIncomingElementAsync(client, unblockPush);
        Assert.False(handledUnblock);
        Assert.False(blocking.IsBlocked("spammer@example.com"));
        Assert.Contains("spammer@example.com", unblockedEventList);
    }

    [Fact]
    public async Task IncomingMessagesAndPresence_FromBlockedEntity_AreDropped()
    {
        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("me@example.com/res"),
            Password = "pass"
        }, transport);

        var blocking = new Xep0191Blocking();
        await blocking.AttachAsync(client);

        // Simulate blocked push
        var push = new XmppElement("iq")
            .Attr("type", "set")
            .Attr("id", "p1")
            .Child(new XmppElement("block", Xep0191Blocking.NsBlocking)
                .Child(new XmppElement("item").Attr("jid", "blocked@example.com")));
        await blocking.OnIncomingElementAsync(client, push);

        // Message from blocked user should be dropped (returns false)
        var msgBlocked = new XmppElement("message")
            .Attr("from", "blocked@example.com/phone")
            .Attr("to", "me@example.com")
            .Child(new XmppElement("body") { Value = "spam!" });
        var passBlocked = await blocking.OnIncomingElementAsync(client, msgBlocked);
        Assert.False(passBlocked);

        // Message from normal user should pass (returns true)
        var msgAllowed = new XmppElement("message")
            .Attr("from", "friend@example.com/pc")
            .Attr("to", "me@example.com")
            .Child(new XmppElement("body") { Value = "hello!" });
        var passAllowed = await blocking.OnIncomingElementAsync(client, msgAllowed);
        Assert.True(passAllowed);

        // Presence from blocked user should also be dropped
        var presBlocked = new XmppElement("presence")
            .Attr("from", "blocked@example.com/phone");
        var passPresBlocked = await blocking.OnIncomingElementAsync(client, presBlocked);
        Assert.False(passPresBlocked);
    }
}
