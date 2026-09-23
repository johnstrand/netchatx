using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class ReadMarkerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;

    public ReadMarkerTests()
    {
        _dbPath = $"testreadmarkers_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
    }

    [Fact]
    public async Task ChatConversationViewModel_1on1_ReadMarkerDividerAndReceiptTooltips()
    {
        string account = "me@example.com";
        var remote = Jid.Parse("alice@example.com");

        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "First outbound message",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            StanzaId = "m1"
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Second outbound message",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            StanzaId = "m2"
        };
        await _messageRepo.SaveMessagesAsync([msg1, msg2]);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Alice",
            remote,
            isGroupChat: false,
            _messageRepo);

        await conv.LoadHistoryAsync();
        Assert.Equal(2, conv.Messages.Count);

        // Before read marker: no divider, tooltip is "Delivered", icon is delivered diamond
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);
        Assert.Equal("Delivered", conv.Messages[0].ReceiptTooltip);
        Assert.False(conv.Messages[0].IsRead);
        Assert.Equal("◇", conv.Messages[0].ReceiptIcon);
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);
        Assert.False(conv.Messages[1].IsRead);
        Assert.Equal("◇", conv.Messages[1].ReceiptIcon);

        // Alice reads msg1
        conv.UpdateReadMarker("alice@example.com", "m1", msg1.Timestamp);

        // msg1 should show read marker divider below it (since msg2 comes after it)
        Assert.True(conv.Messages[0].ShowReadMarkerDivider);
        Assert.Equal("Read by Alice", conv.Messages[0].ReadMarkerDividerText);
        Assert.Equal("Read by Alice", conv.Messages[0].ReceiptTooltip);
        Assert.True(conv.Messages[0].IsRead);
        Assert.Equal("◈", conv.Messages[0].ReceiptIcon);

        // msg2 is unread
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);
        Assert.Equal("Delivered", conv.Messages[1].ReceiptTooltip);
        Assert.False(conv.Messages[1].IsRead);
        Assert.Equal("◇", conv.Messages[1].ReceiptIcon);

        // Alice reads msg2
        conv.UpdateReadMarker("alice@example.com", "m2", msg2.Timestamp);

        // When all messages up to the last one are read, divider is hidden (no unread messages after it)
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);

        // Both checkmark tooltips show "Read by Alice" and read diamonds
        Assert.Equal("Read by Alice", conv.Messages[0].ReceiptTooltip);
        Assert.Equal("Read by Alice", conv.Messages[1].ReceiptTooltip);
        Assert.True(conv.Messages[0].IsRead);
        Assert.Equal("◈", conv.Messages[0].ReceiptIcon);
        Assert.True(conv.Messages[1].IsRead);
        Assert.Equal("◈", conv.Messages[1].ReceiptIcon);
    }

    [Fact]
    public async Task ChatConversationViewModel_GroupChat_ReadMarkerDividerAndParticipantTooltips()
    {
        string account = "me@example.com";
        var remote = Jid.Parse("room@conference.example.com");

        var msg1 = new ChatMessage
        {
            Id = "g1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Hello room",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-20),
            StanzaId = "g1"
        };
        var msg2 = new ChatMessage
        {
            Id = "g2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Second room message",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            StanzaId = "g2"
        };
        await _messageRepo.SaveMessagesAsync([msg1, msg2]);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Developers Room",
            remote,
            isGroupChat: true,
            _messageRepo);

        await conv.LoadHistoryAsync();
        Assert.Equal(2, conv.Messages.Count);

        // Alice and Bob read g1
        conv.UpdateReadMarker("room@conference.example.com/alice", "g1", msg1.Timestamp);
        conv.UpdateReadMarker("room@conference.example.com/bob", "g1", msg1.Timestamp);

        // Everyone (Alice and Bob) has read g1, and g2 is unread after g1 -> show "Read by everyone" divider on g1
        Assert.True(conv.Messages[0].ShowReadMarkerDivider);
        Assert.Equal("Read by everyone", conv.Messages[0].ReadMarkerDividerText);

        // Tooltip on g1: "Read by everyone (alice, bob)" or "Read by: alice, bob"
        Assert.NotNull(conv.Messages[0].ReceiptTooltip);
        Assert.Contains("alice", conv.Messages[0].ReceiptTooltip);
        Assert.Contains("bob", conv.Messages[0].ReceiptTooltip);
        Assert.True(conv.Messages[0].IsRead);
        Assert.Equal("◈", conv.Messages[0].ReceiptIcon);

        // Tooltip on g2: "Delivered" (neither has read g2)
        Assert.Equal("Delivered", conv.Messages[1].ReceiptTooltip);
        Assert.False(conv.Messages[1].IsRead);
        Assert.Equal("◇", conv.Messages[1].ReceiptIcon);
    }

    [Fact]
    public async Task ChatConversationViewModel_PersistsAndRestoresReadMarkersFromDatabase()
    {
        string account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var msg = new ChatMessage
        {
            Id = "m_persisted_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Persisted read marker msg",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-15),
            StanzaId = "s_persisted_1"
        };
        var msg2 = new ChatMessage
        {
            Id = "m_persisted_2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Newer msg",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            StanzaId = "s_persisted_2"
        };
        await _messageRepo.SaveMessagesAsync([msg, msg2]);

        // Save read marker in DB
        await _messageRepo.SaveReadMarkerAsync(account, remote.ToString(), remote.ToString(), "m_persisted_1", msg.Timestamp);

        // Load conversation
        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        await conv.LoadHistoryAsync();

        Assert.True(conv.Messages[0].ShowReadMarkerDivider);
        Assert.Equal("Read by Peer", conv.Messages[0].ReadMarkerDividerText);
        Assert.Equal("Read by Peer", conv.Messages[0].ReceiptTooltip);
        Assert.True(conv.Messages[0].IsRead);
        Assert.Equal("◈", conv.Messages[0].ReceiptIcon);
        Assert.Equal("Delivered", conv.Messages[1].ReceiptTooltip);
        Assert.False(conv.Messages[1].IsRead);
        Assert.Equal("◇", conv.Messages[1].ReceiptIcon);
    }

    [Fact]
    public async Task ChatConversationViewModel_InboundReply_MarksPriorOutboundAsReadAndDividerBehavesCorrectly()
    {
        string account = "me@example.com";
        var remote = Jid.Parse("alice@example.com");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Alice",
            remote,
            isGroupChat: false,
            _messageRepo);

        // 1. User sends outbound message 1
        var out1 = new ChatMessage
        {
            Id = "out1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Hey Alice",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            StanzaId = "out1"
        };
        conv.AddOrUpdateMessage(out1);

        Assert.Single(conv.Messages);
        Assert.False(conv.Messages[0].IsRead);
        Assert.Equal("◇", conv.Messages[0].ReceiptIcon);
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);

        // 2. Alice sends an inbound reply
        var in1 = new ChatMessage
        {
            Id = "in1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = "alice@example.com",
            Body = "Hello there!",
            Direction = MessageDirection.Inbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-8),
            StanzaId = "in1"
        };
        conv.AddOrUpdateMessage(in1);

        Assert.Equal(2, conv.Messages.Count);
        // Outbound message 1 is now confirmed read by Alice
        Assert.True(conv.Messages[0].IsRead);
        Assert.Equal("◈", conv.Messages[0].ReceiptIcon);
        Assert.Equal("Read by Alice", conv.Messages[0].ReceiptTooltip);
        // All messages read up to the latest message, so divider is hidden
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);

        // 3. User sends outbound message 2
        var out2 = new ChatMessage
        {
            Id = "out2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "How are things?",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            StanzaId = "out2"
        };
        conv.AddOrUpdateMessage(out2);

        Assert.Equal(3, conv.Messages.Count);
        // in1 is now the last read message by Alice, out2 is unread
        Assert.True(conv.Messages[0].IsRead);
        Assert.Equal("◈", conv.Messages[0].ReceiptIcon);
        Assert.False(conv.Messages[2].IsRead);
        Assert.Equal("◇", conv.Messages[2].ReceiptIcon);
        Assert.Equal("Delivered", conv.Messages[2].ReceiptTooltip);

        // Divider is shown below in1 (the last read message)
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);
        Assert.True(conv.Messages[1].ShowReadMarkerDivider);
        Assert.Equal("Read by Alice", conv.Messages[1].ReadMarkerDividerText);
        Assert.False(conv.Messages[2].ShowReadMarkerDivider);

        // 4. Alice reads out2 via read marker
        conv.UpdateReadMarker("alice@example.com", "out2", out2.Timestamp);

        // Now out2 is read as well
        Assert.True(conv.Messages[2].IsRead);
        Assert.Equal("◈", conv.Messages[2].ReceiptIcon);
        Assert.Equal("Read by Alice", conv.Messages[2].ReceiptTooltip);

        // All messages have been read, divider is hidden everywhere
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);
        Assert.False(conv.Messages[2].ShowReadMarkerDivider);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
