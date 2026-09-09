using NetChatx.Core;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using NetChatx.Tui.Commands;
using NetChatx.Tui.Models;
using Xunit;

namespace NetChatx.Storage.Tests;

public class ChatHistoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _context;

    public ChatHistoryTests()
    {
        _dbPath = $"testhistory_{Guid.NewGuid():N}.db";
        _context = new DatabaseContext(_dbPath);
    }

    [Fact]
    public void ChatBuffer_LoadHistory_PopulatesDisplayLinesAndTracksOldestTimestamp()
    {
        var buffer = new ChatBuffer("alice@example.com", "Alice", BufferType.DirectChat, Jid.Parse("alice@example.com"));
        Assert.False(buffer.HasLoadedHistory);
        Assert.Null(buffer.OldestMessageTimestamp);

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var messages = new List<ChatMessage>
        {
            new()
            {
                AccountJid = "me@example.com",
                RemoteJid = "alice@example.com",
                SenderJid = "alice@example.com",
                Timestamp = t0,
                Direction = MessageDirection.Inbound,
                Body = "Hello there!"
            },
            new()
            {
                AccountJid = "me@example.com",
                RemoteJid = "alice@example.com",
                SenderJid = "me@example.com",
                Timestamp = t0.AddMinutes(5),
                Direction = MessageDirection.Outbound,
                Body = "Hi Alice! How are you?",
                IsRead = true
            }
        };

        buffer.LoadHistory(messages);

        Assert.True(buffer.HasLoadedHistory);
        Assert.Equal(t0, buffer.OldestMessageTimestamp);
        Assert.Equal(2, buffer.DisplayLines.Count);
        Assert.Contains("Hello there!", buffer.DisplayLines[0]);
        Assert.Contains("<alice>", buffer.DisplayLines[0]);
        Assert.Contains("Hi Alice! How are you?", buffer.DisplayLines[1]);
        Assert.Contains("<Me>", buffer.DisplayLines[1]);
        Assert.Contains("✓✓", buffer.DisplayLines[1]); // read receipt
    }

    [Fact]
    public void ChatBuffer_PrependHistory_MaintainsChronologicalOrderAndUpdatesOldestTimestamp()
    {
        var buffer = new ChatBuffer("alice@example.com", "Alice", BufferType.DirectChat, Jid.Parse("alice@example.com"));
        var t0 = DateTimeOffset.UtcNow;

        var initialMessages = new List<ChatMessage>
        {
            new()
            {
                AccountJid = "me@example.com",
                RemoteJid = "alice@example.com",
                SenderJid = "me@example.com",
                Timestamp = t0.AddMinutes(-10),
                Direction = MessageDirection.Outbound,
                Body = "Recent message"
            }
        };

        buffer.LoadHistory(initialMessages);
        Assert.Equal(t0.AddMinutes(-10), buffer.OldestMessageTimestamp);

        var olderMessages = new List<ChatMessage>
        {
            new()
            {
                AccountJid = "me@example.com",
                RemoteJid = "alice@example.com",
                SenderJid = "alice@example.com",
                Timestamp = t0.AddMinutes(-60),
                Direction = MessageDirection.Inbound,
                Body = "Very old message 1"
            },
            new()
            {
                AccountJid = "me@example.com",
                RemoteJid = "alice@example.com",
                SenderJid = "alice@example.com",
                Timestamp = t0.AddMinutes(-50),
                Direction = MessageDirection.Inbound,
                Body = "Very old message 2"
            }
        };

        buffer.PrependHistory(olderMessages);

        Assert.Equal(3, buffer.DisplayLines.Count);
        Assert.Equal(t0.AddMinutes(-60), buffer.OldestMessageTimestamp);
        Assert.Contains("Very old message 1", buffer.DisplayLines[0]);
        Assert.Contains("Very old message 2", buffer.DisplayLines[1]);
        Assert.Contains("Recent message", buffer.DisplayLines[2]);
    }

    [Fact]
    public void TuiCommandProcessor_ParsesHistoryAndSearchCommands()
    {
        var cmd1 = TuiCommandProcessor.Parse("/history");
        Assert.Equal("/history", cmd1.Command);
        Assert.Empty(cmd1.Arguments);

        var cmd2 = TuiCommandProcessor.Parse("/history 100");
        Assert.Equal("/history", cmd2.Command);
        Assert.Single(cmd2.Arguments);
        Assert.Equal("100", cmd2.Arguments[0]);

        var cmd3 = TuiCommandProcessor.Parse("/search \"secret project\"");
        Assert.Equal("/search", cmd3.Command);
        Assert.Single(cmd3.Arguments);
        Assert.Equal("secret project", cmd3.Arguments[0]);

        var cmd4 = TuiCommandProcessor.Parse("/query bob@example.com");
        Assert.Equal("/query", cmd4.Command);
        Assert.Single(cmd4.Arguments);
        Assert.Equal("bob@example.com", cmd4.Arguments[0]);

        // Autocomplete
        var completions = TuiCommandProcessor.GetCompletions("/his", []);
        Assert.Contains("/history", completions);

        var searchCompletions = TuiCommandProcessor.GetCompletions("/sea", []);
        Assert.Contains("/search", searchCompletions);
    }

    [Fact]
    public async Task MessageRepository_SearchMessages_FindsMatchingText()
    {
        var repo = new MessageRepository(_context);
        string account = "user@test.org";

        await repo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = "contact1@test.org",
            SenderJid = "contact1@test.org",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            Direction = MessageDirection.Inbound,
            Body = "Meeting is scheduled at 3 PM tomorrow."
        });

        await repo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = "contact2@test.org",
            SenderJid = "contact2@test.org",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2),
            Direction = MessageDirection.Inbound,
            Body = "Where are the deployment logs?"
        });

        var results = await repo.SearchMessagesAsync(account, "scheduled");
        Assert.Single(results);
        Assert.Equal("contact1@test.org", results[0].RemoteJid);
        Assert.Contains("scheduled at 3 PM", results[0].Body);

        var noResults = await repo.SearchMessagesAsync(account, "nonexistent");
        Assert.Empty(noResults);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
