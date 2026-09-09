using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using Xunit.Abstractions;

namespace NetChatx.Storage.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _context;
    private readonly ITestOutputHelper _output;

    public StorageTests(ITestOutputHelper output)
    {
        _output = output;
        _dbPath = $"test_{Guid.NewGuid():N}.db";
        _context = new DatabaseContext(_dbPath);
    }

    [Fact]
    public async Task AccountRepository_Crud_Succeeds()
    {
        var repo = new AccountRepository(_context);
        var account = new AccountProfile
        {
            Jid = "alice@example.com",
            Password = "secretpassword",
            Resource = "CustomRes",
            Host = "xmpp.example.com",
            Port = 5222,
            UseDirectTls = false,
            IsActive = true
        };

        await repo.SaveAccountAsync(account);

        var retrieved = await repo.GetAccountAsync("alice@example.com");
        Assert.NotNull(retrieved);
        Assert.Equal("secretpassword", retrieved.Password);
        Assert.Equal("CustomRes", retrieved.Resource);
        Assert.Equal("xmpp.example.com", retrieved.Host);

        var all = await repo.GetAccountsAsync();
        Assert.Single(all);

        await repo.DeleteAccountAsync("alice@example.com");
        var deleted = await repo.GetAccountAsync("alice@example.com");
        Assert.Null(deleted);
    }

    [Fact]
    public async Task MessageRepository_SaveAndGetPaged_ReturnsChronologicalOrder()
    {
        var repo = new MessageRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var msg1 = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = remote,
            Timestamp = t0,
            Direction = MessageDirection.Inbound,
            Body = "First message",
            StanzaId = "s1"
        };

        var msg2 = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = account,
            Timestamp = t0.AddMinutes(1),
            Direction = MessageDirection.Outbound,
            Body = "Second message",
            StanzaId = "s2"
        };

        await repo.SaveMessageAsync(msg1);
        await repo.SaveMessageAsync(msg2);

        var messages = await repo.GetMessagesAsync(account, remote, limit: 10);
        Assert.Equal(2, messages.Count);
        Assert.Equal("First message", messages[0].Body);
        Assert.Equal("Second message", messages[1].Body);

        // Test search
        var searchResults = await repo.SearchMessagesAsync(account, "Second");
        Assert.Single(searchResults);
        Assert.Equal("Second message", searchResults[0].Body);

        // Test replacement (XEP-0308)
        bool replaced = await repo.UpdateMessageByReplaceIdAsync(account, "s1", "First message (edited)");
        Assert.True(replaced);

        var updatedMessages = await repo.GetMessagesAsync(account, remote, limit: 10);
        Assert.Equal("First message (edited)", updatedMessages[0].Body);
    }

    [Fact]
    public async Task MessageRepository_GetMessagesWithBeforePaging_ReturnsOlderBatches()
    {
        var repo = new MessageRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";

        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);

        for (int i = 0; i < 20; i++)
        {
            await repo.SaveMessageAsync(new ChatMessage
            {
                AccountJid = account,
                RemoteJid = remote,
                SenderJid = (i % 2 == 0) ? account : remote,
                Timestamp = baseTime.AddMinutes(i),
                Direction = (i % 2 == 0) ? MessageDirection.Outbound : MessageDirection.Inbound,
                Body = $"Message #{i}",
                StanzaId = $"id_{i}"
            });
        }

        // Fetch latest 5 (messages 15 to 19)
        var latest5 = await repo.GetMessagesAsync(account, remote, limit: 5);
        Assert.Equal(5, latest5.Count);
        Assert.Equal("Message #15", latest5[0].Body);
        Assert.Equal("Message #19", latest5[4].Body);

        // Fetch older 5 before the oldest in latest5 (Message #15's timestamp)
        var older5 = await repo.GetMessagesAsync(account, remote, limit: 5, before: latest5[0].Timestamp);
        Assert.Equal(5, older5.Count);
        Assert.Equal("Message #10", older5[0].Body);
        Assert.Equal("Message #14", older5[4].Body);

        // Fetch older 5 before Message #10's timestamp
        var evenOlder5 = await repo.GetMessagesAsync(account, remote, limit: 5, before: older5[0].Timestamp);
        Assert.Equal(5, evenOlder5.Count);
        Assert.Equal("Message #5", evenOlder5[0].Body);
        Assert.Equal("Message #9", evenOlder5[4].Body);
    }

    [Fact]
    public async Task MessageRepository_SaveMessageAsync_DeduplicatesByStanzaIdAndContent()
    {
        var repo = new MessageRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";
        var t0 = DateTimeOffset.UtcNow.AddHours(-3);

        // 1. Save initial message
        var msg1 = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = remote,
            Timestamp = t0,
            Direction = MessageDirection.Inbound,
            Body = "Sync message",
            StanzaId = "archive_item_99"
        };
        await repo.SaveMessageAsync(msg1);

        var list1 = await repo.GetMessagesAsync(account, remote);
        Assert.Single(list1);
        Assert.Equal("Sync message", list1[0].Body);

        // 2. Save identical message again with different GUID id (simulating multiple sync clicks)
        var msgDuplicate = new ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"), // different ID
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = remote,
            Timestamp = t0,
            Direction = MessageDirection.Inbound,
            Body = "Sync message",
            StanzaId = "archive_item_99" // same archive/stanza ID
        };
        await repo.SaveMessageAsync(msgDuplicate);

        // Verify still only ONE message in database
        var list2 = await repo.GetMessagesAsync(account, remote);
        Assert.Single(list2);

        // 3. Save without stanza_id but matching timestamp & body
        var msgContentMatch = new ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = remote,
            Timestamp = t0,
            Direction = MessageDirection.Inbound,
            Body = "Sync message"
        };
        await repo.SaveMessageAsync(msgContentMatch);

        var list3 = await repo.GetMessagesAsync(account, remote);
        Assert.Single(list3);

        // 4. Save MAM message with slight clock drift (+3 seconds) and server archive ID - should update existing message, not duplicate
        var msgDriftMAM = new ChatMessage
        {
            Id = $"mam_{account}_{remote}_arch99",
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = remote,
            Timestamp = t0.AddSeconds(3),
            Direction = MessageDirection.Inbound,
            Body = "Sync message",
            StanzaId = "arch99"
        };
        await repo.SaveMessageAsync(msgDriftMAM);

        var list4 = await repo.GetMessagesAsync(account, remote);
        Assert.Single(list4);
        Assert.Equal("arch99", list4[0].StanzaId);
    }

    [Fact]
    public async Task RosterRepository_UpsertAndGet_Succeeds()
    {
        var repo = new RosterRepository(_context);
        string account = "alice@example.com";

        var contact = new RosterContact
        {
            AccountJid = account,
            ContactJid = "charlie@example.com",
            Name = "Charlie Brown",
            Subscription = "both",
            Groups = "Friends;Family"
        };

        await repo.UpsertContactAsync(contact);

        var contacts = await repo.GetContactsAsync(account);
        Assert.Single(contacts);
        Assert.Equal("Charlie Brown", contacts[0].Name);
        Assert.Equal("both", contacts[0].Subscription);
    }

    [Fact]
    public async Task MessageRepository_SaveMessagesAsync_SavesAndDeduplicatesCorrectly()
    {
        var repo = new MessageRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";
        var t0 = DateTimeOffset.UtcNow.AddHours(-1);

        var batch1 = new List<ChatMessage>
        {
            new ChatMessage
            {
                Id = "msg_1",
                AccountJid = account,
                RemoteJid = remote,
                SenderJid = remote,
                Timestamp = t0,
                Direction = MessageDirection.Inbound,
                Body = "Batch message 1",
                StanzaId = "st_1"
            },
            new ChatMessage
            {
                Id = "msg_2",
                AccountJid = account,
                RemoteJid = remote,
                SenderJid = account,
                Timestamp = t0.AddSeconds(5),
                Direction = MessageDirection.Outbound,
                Body = "Batch message 2",
                StanzaId = "st_2"
            }
        };

        await repo.SaveMessagesAsync(batch1);

        var retrieved1 = await repo.GetMessagesAsync(account, remote);
        Assert.Equal(2, retrieved1.Count);
        Assert.Equal("Batch message 1", retrieved1[0].Body);
        Assert.Equal("Batch message 2", retrieved1[1].Body);

        // Batch duplicate with new IDs but same stanza IDs
        var batchDuplicate = new List<ChatMessage>
        {
            new ChatMessage
            {
                Id = "new_guid_1",
                AccountJid = account,
                RemoteJid = remote,
                SenderJid = remote,
                Timestamp = t0,
                Direction = MessageDirection.Inbound,
                Body = "Batch message 1",
                StanzaId = "st_1"
            }
        };

        await repo.SaveMessagesAsync(batchDuplicate);

        var retrieved2 = await repo.GetMessagesAsync(account, remote);
        Assert.Equal(2, retrieved2.Count);
    }

    [Fact]
    public async Task MessageRepository_PerformanceBenchmark()
    {
        var repo = new MessageRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";
        int messageCount = 500;

        var messagesBatch = new List<ChatMessage>(messageCount);
        var baseTime = DateTimeOffset.UtcNow.AddDays(-1);
        for (int i = 0; i < messageCount; i++)
        {
            messagesBatch.Add(new ChatMessage
            {
                Id = $"bench_{i}",
                AccountJid = account,
                RemoteJid = remote,
                SenderJid = (i % 2 == 0) ? account : remote,
                Timestamp = baseTime.AddSeconds(i),
                Direction = (i % 2 == 0) ? MessageDirection.Outbound : MessageDirection.Inbound,
                Body = $"Benchmark message payload test #{i}",
                StanzaId = $"stanza_{i}"
            });
        }

        var swBatch = System.Diagnostics.Stopwatch.StartNew();
        await repo.SaveMessagesAsync(messagesBatch);
        swBatch.Stop();

        _output.WriteLine($"[OPTIMIZED] SaveMessagesAsync ({messageCount} msgs): {swBatch.ElapsedMilliseconds} ms");

        var retrieved = await repo.GetMessagesAsync(account, remote, limit: messageCount + 10);
        Assert.Equal(messageCount, retrieved.Count);
    }

    [Fact]
    public void DatabaseContext_DefaultPath_IsInUserDataDirectory()
    {
        var defaultPath = DatabaseContext.GetDefaultDatabasePath();
        Assert.NotNull(defaultPath);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.StartsWith(appData, defaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("netchatx.db", defaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.GetDirectoryName(defaultPath)));
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
