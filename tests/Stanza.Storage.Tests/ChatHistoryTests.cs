using Stanza.Core;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Storage.Tests;

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
        var account = "user@test.org";

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

    [Fact]
    public async Task MessageRepository_SearchMessages_SupportsAccountScopingAndAllAccounts()
    {
        var repo = new MessageRepository(_context);
        var acc1 = "alice@example.com";
        var acc2 = "bob@example.com";

        await repo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = acc1,
            RemoteJid = "charlie@example.com",
            SenderJid = "charlie@example.com",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            Direction = MessageDirection.Inbound,
            Body = "Important project update for Alice"
        });

        await repo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = acc2,
            RemoteJid = "david@example.com",
            SenderJid = "david@example.com",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2),
            Direction = MessageDirection.Inbound,
            Body = "Important project update for Bob"
        });

        // Search specifically for Alice
        var aliceResults = await repo.SearchMessagesAsync(acc1, "project update");
        Assert.Single(aliceResults);
        Assert.Equal(acc1, aliceResults[0].AccountJid);
        Assert.Contains("for Alice", aliceResults[0].Body);

        // Search specifically for Bob
        var bobResults = await repo.SearchMessagesAsync(acc2, "project update");
        Assert.Single(bobResults);
        Assert.Equal(acc2, bobResults[0].AccountJid);
        Assert.Contains("for Bob", bobResults[0].Body);

        // Search across all accounts (null or empty accountJid)
        var allResults = await repo.SearchMessagesAsync(null, "project update");
        Assert.Equal(2, allResults.Count);
        Assert.Contains(allResults, m => m.AccountJid == acc1 && m.Body.Contains("for Alice"));
        Assert.Contains(allResults, m => m.AccountJid == acc2 && m.Body.Contains("for Bob"));
    }

    [Fact]
    public async Task MessageRepository_RawXml_SavesAndRetrievesXml()
    {
        var repo = new MessageRepository(_context);
        var account = "user@test.org";
        var expectedXml = "<message to='remote@test.org' type='chat'><body>Test XML message</body></message>";

        var msg = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = "remote@test.org",
            SenderJid = account,
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Outbound,
            Body = "Test XML message",
            RawXml = expectedXml
        };

        await repo.SaveMessageAsync(msg);

        var history = await repo.GetMessagesAsync(account, "remote@test.org");
        Assert.Single(history);
        Assert.Equal(expectedXml, history[0].RawXml);

        var searchResult = await repo.SearchMessagesAsync(account, "Test XML");
        Assert.Single(searchResult);
        Assert.Equal(expectedXml, searchResult[0].RawXml);
    }

    [Fact]
    public async Task MessageRepository_ReadMarkers_SavesAndRetrievesReadMarkers()
    {
        var repo = new MessageRepository(_context);
        string account = "user@test.org";
        string remote = "group@conference.test.org";
        string participant1 = "alice@test.org";
        string participant2 = "bob@test.org";

        var msg = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = "alice@test.org",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound,
            Body = "Hello group",
            StanzaId = "msg_100"
        };
        await repo.SaveMessageAsync(msg);

        await repo.SaveReadMarkerAsync(account, remote, participant1, "msg_100");
        await repo.SaveReadMarkerAsync(account, remote, participant2, "msg_100");

        var markers = await repo.GetReadMarkersAsync(account, remote);
        Assert.Equal(2, markers.Count);
        Assert.Contains(markers, m => m.ParticipantJid == participant1 && m.LastReadMessageId == msg.Id);
        Assert.Contains(markers, m => m.ParticipantJid == participant2 && m.LastReadMessageId == msg.Id);
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
