using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetChatx.Core;
using NetChatx.Gui.ViewModels;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using Xunit;

namespace NetChatx.Gui.Tests;

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

        // Before read marker: no divider, tooltip is "Delivered"
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);
        Assert.Equal("Delivered", conv.Messages[0].ReceiptTooltip);
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);

        // Alice reads msg1
        conv.UpdateReadMarker("alice@example.com", "m1", msg1.Timestamp);

        // msg1 should show read marker divider below it (since msg2 comes after it)
        Assert.True(conv.Messages[0].ShowReadMarkerDivider);
        Assert.Equal("Read by Alice", conv.Messages[0].ReadMarkerDividerText);
        Assert.Equal("Read by Alice", conv.Messages[0].ReceiptTooltip);

        // msg2 is unread
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);
        Assert.Equal("Delivered", conv.Messages[1].ReceiptTooltip);

        // Alice reads msg2
        conv.UpdateReadMarker("alice@example.com", "m2", msg2.Timestamp);

        // When all messages up to the last one are read, divider is hidden (no unread messages after it)
        Assert.False(conv.Messages[0].ShowReadMarkerDivider);
        Assert.False(conv.Messages[1].ShowReadMarkerDivider);

        // Both checkmark tooltips show "Read by Alice"
        Assert.Equal("Read by Alice", conv.Messages[0].ReceiptTooltip);
        Assert.Equal("Read by Alice", conv.Messages[1].ReceiptTooltip);
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

        // Tooltip on g2: "Delivered" (neither has read g2)
        Assert.Equal("Delivered", conv.Messages[1].ReceiptTooltip);
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
        Assert.Equal("Delivered", conv.Messages[1].ReceiptTooltip);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
