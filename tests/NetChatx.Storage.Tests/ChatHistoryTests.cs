using NetChatx.Core;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
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
