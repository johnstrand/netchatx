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
    public async Task MessageRepository_ReactionsSaveAndRetrieve_WorksCorrectly()
    {
        var repo = new MessageRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";
        string msgId = "msg_reaction_test_1";

        await repo.SaveMessageAsync(new ChatMessage
        {
            Id = msgId,
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = remote,
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound,
            Body = "Test message for reactions",
            StanzaId = "stanza_react_1"
        });

        // 1. Bob reacts with 👍 and ❤️
        await repo.SaveReactionsAsync(account, remote, "stanza_react_1", "bob@example.com", ["👍", "❤️"]);

        var reactions1 = await repo.GetReactionsForMessagesAsync(account, [msgId]);
        Assert.Equal(2, reactions1.Count);
        Assert.Contains(reactions1, r => r.SenderJid == "bob@example.com" && r.Emoji == "👍");
        Assert.Contains(reactions1, r => r.SenderJid == "bob@example.com" && r.Emoji == "❤️");

        // 2. Alice reacts with 👍
        await repo.SaveReactionsAsync(account, remote, msgId, "alice@example.com", ["👍"]);

        var reactions2 = await repo.GetReactionsForMessagesAsync(account, [msgId]);
        Assert.Equal(3, reactions2.Count);

        // 4. Bob reacts from a different resource 'bob@example.com/mobile' with 🎉
        // In 1:1 chat, sender JID is normalized so this replaces Bob's previous 👍 reaction rather than duplicating
        await repo.SaveReactionsAsync(account, remote, "stanza_react_1", "bob@example.com/mobile", ["🎉"]);

        var reactions4 = await repo.GetReactionsForMessagesAsync(account, [msgId]);
        Assert.Equal(2, reactions4.Count);
        Assert.Contains(reactions4, r => r.SenderJid == "bob@example.com" && r.Emoji == "🎉");
        Assert.Contains(reactions4, r => r.SenderJid == "alice@example.com" && r.Emoji == "👍");
    }

    [Fact]
    public async Task SettingsRepository_QuickEmojis_SaveAndRetrieve_Succeeds()
    {
        var repo = new SettingsRepository(_context);
        string account = "user@example.org";

        // 1. Defaults when nothing is configured
        var defaults = await repo.GetQuickEmojisAsync(account);
        Assert.Equal(SettingsRepository.DefaultQuickEmojis, defaults);

        // 2. Save custom quick emojis
        string[] custom = ["🚀", "🔥", "💯", "👏"];
        await repo.SetQuickEmojisAsync(account, custom);

        var retrieved = await repo.GetQuickEmojisAsync(account);
        Assert.Equal(custom, retrieved);

        // 3. Independent per account
        string otherAccount = "other@example.org";
        var otherDefaults = await repo.GetQuickEmojisAsync(otherAccount);
        Assert.Equal(SettingsRepository.DefaultQuickEmojis, otherDefaults);
    }

    [Fact]
    public async Task SettingsRepository_NotificationSettings_SaveAndRetrieve_Succeeds()
    {
        var repo = new SettingsRepository(_context);
        string account = "notify_user@example.org";

        // Defaults
        Assert.True(await repo.GetNotificationPopupsEnabledAsync(account));
        Assert.True(await repo.GetIconFlashingEnabledAsync(account));

        // Update to false
        await repo.SetNotificationPopupsEnabledAsync(account, false);
        await repo.SetIconFlashingEnabledAsync(account, false);

        Assert.False(await repo.GetNotificationPopupsEnabledAsync(account));
        Assert.False(await repo.GetIconFlashingEnabledAsync(account));

        // Update back to true
        await repo.SetNotificationPopupsEnabledAsync(account, true);
        await repo.SetIconFlashingEnabledAsync(account, true);

        Assert.True(await repo.GetNotificationPopupsEnabledAsync(account));
        Assert.True(await repo.GetIconFlashingEnabledAsync(account));
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
    public async Task OmemoRepository_SaveAndGetIdentity_Succeeds()
    {
        var repo = new OmemoRepository(_context);
        string account = "alice@example.com";

        // Non-existent identity returns null
        var nonExistent = await repo.GetIdentityAsync("nonexistent@example.com");
        Assert.Null(nonExistent);

        // Save identity
        await repo.SaveIdentityAsync(account, 12345, "priv_key_1", "pub_key_1");

        // Retrieve identity
        var identity = await repo.GetIdentityAsync(account);
        Assert.NotNull(identity);
        Assert.Equal(12345, identity.Value.DeviceId);
        Assert.Equal("priv_key_1", identity.Value.PrivateKey);
        Assert.Equal("pub_key_1", identity.Value.PublicKey);

        // Upsert / update identity on conflict
        await repo.SaveIdentityAsync(account, 54321, "priv_key_2", "pub_key_2");

        var updatedIdentity = await repo.GetIdentityAsync(account);
        Assert.NotNull(updatedIdentity);
        Assert.Equal(54321, updatedIdentity.Value.DeviceId);
        Assert.Equal("priv_key_2", updatedIdentity.Value.PrivateKey);
        Assert.Equal("pub_key_2", updatedIdentity.Value.PublicKey);
    }

    [Fact]
    public async Task OmemoRepository_SaveAndGetSession_Succeeds()
    {
        var repo = new OmemoRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";

        // Non-existent session returns null
        var nonExistent = await repo.GetSessionAsync(account, remote, 100);
        Assert.Null(nonExistent);

        // Non-existent sessions list returns empty list
        var emptyList = await repo.GetSessionsAsync(account, remote);
        Assert.Empty(emptyList);

        var now = DateTimeOffset.UtcNow;
        // Truncate sub-second precision to match ISO string roundtrip accuracy if needed
        var nowTruncated = DateTimeOffset.Parse(now.ToString("O"));

        var session1 = new OmemoSessionRecord
        {
            AccountJid = account,
            RemoteJid = remote,
            DeviceId = 100,
            SessionData = new byte[] { 0x01, 0x02, 0x03, 0x04 },
            LastActive = nowTruncated,
            TrustState = OmemoTrustState.Undecided
        };

        var session2 = new OmemoSessionRecord
        {
            AccountJid = account,
            RemoteJid = remote,
            DeviceId = 200,
            SessionData = new byte[] { 0x05, 0x06, 0x07, 0x08 },
            LastActive = nowTruncated,
            TrustState = OmemoTrustState.Trusted
        };

        await repo.SaveSessionAsync(session1);
        await repo.SaveSessionAsync(session2);

        // Get single session
        var fetched1 = await repo.GetSessionAsync(account, remote, 100);
        Assert.NotNull(fetched1);
        Assert.Equal(account, fetched1.AccountJid);
        Assert.Equal(remote, fetched1.RemoteJid);
        Assert.Equal(100, fetched1.DeviceId);
        Assert.Equal(session1.SessionData, fetched1.SessionData);
        Assert.Equal(nowTruncated, fetched1.LastActive);
        Assert.Equal(OmemoTrustState.Undecided, fetched1.TrustState);

        // Get all sessions for account & remote
        var sessions = await repo.GetSessionsAsync(account, remote);
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.DeviceId == 100);
        Assert.Contains(sessions, s => s.DeviceId == 200);

        // Upsert/update session
        var updatedSession1 = new OmemoSessionRecord
        {
            AccountJid = account,
            RemoteJid = remote,
            DeviceId = 100,
            SessionData = new byte[] { 0x0A, 0x0B, 0x0C },
            LastActive = nowTruncated.AddMinutes(5),
            TrustState = OmemoTrustState.Trusted
        };

        await repo.SaveSessionAsync(updatedSession1);

        var refetched1 = await repo.GetSessionAsync(account, remote, 100);
        Assert.NotNull(refetched1);
        Assert.Equal(new byte[] { 0x0A, 0x0B, 0x0C }, refetched1.SessionData);
        Assert.Equal(nowTruncated.AddMinutes(5), refetched1.LastActive);
        Assert.Equal(OmemoTrustState.Trusted, refetched1.TrustState);
    }

    [Fact]
    public async Task OmemoRepository_UpdateTrustState_Succeeds()
    {
        var repo = new OmemoRepository(_context);
        string account = "alice@example.com";
        string remote = "bob@example.com";
        uint deviceId = 300;

        var session = new OmemoSessionRecord
        {
            AccountJid = account,
            RemoteJid = remote,
            DeviceId = (int)deviceId,
            SessionData = new byte[] { 0x10, 0x20 },
            LastActive = DateTimeOffset.UtcNow,
            TrustState = OmemoTrustState.Undecided
        };

        await repo.SaveSessionAsync(session);

        var initial = await repo.GetSessionAsync(account, remote, (int)deviceId);
        Assert.NotNull(initial);
        Assert.Equal(OmemoTrustState.Undecided, initial.TrustState);

        // Update trust state
        await repo.UpdateTrustStateAsync(account, remote, deviceId, OmemoTrustState.Untrusted);

        var updated = await repo.GetSessionAsync(account, remote, (int)deviceId);
        Assert.NotNull(updated);
        Assert.Equal(OmemoTrustState.Untrusted, updated.TrustState);
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
    public async Task RosterRepository_RemoveContact_RemovesSpecifiedContactOnly()
    {
        var repo = new RosterRepository(_context);
        string account1 = "alice@example.com";
        string account2 = "bob@example.com";

        var contact1 = new RosterContact
        {
            AccountJid = account1,
            ContactJid = "charlie@example.com",
            Name = "Charlie",
            Subscription = "both"
        };

        var contact2 = new RosterContact
        {
            AccountJid = account1,
            ContactJid = "dave@example.com",
            Name = "Dave",
            Subscription = "to"
        };

        var contact3 = new RosterContact
        {
            AccountJid = account2,
            ContactJid = "charlie@example.com",
            Name = "Charlie",
            Subscription = "both"
        };

        await repo.UpsertContactAsync(contact1);
        await repo.UpsertContactAsync(contact2);
        await repo.UpsertContactAsync(contact3);

        // Verify initial state
        var account1ContactsInitial = await repo.GetContactsAsync(account1);
        Assert.Equal(2, account1ContactsInitial.Count);

        // Attempt removing a non-existent contact for account1
        await repo.RemoveContactAsync(account1, "nonexistent@example.com");
        var account1ContactsAfterNonExistent = await repo.GetContactsAsync(account1);
        Assert.Equal(2, account1ContactsAfterNonExistent.Count);

        // Remove charlie from account1
        await repo.RemoveContactAsync(account1, "charlie@example.com");

        // Verify account1 contacts (only Dave remains)
        var account1ContactsFinal = await repo.GetContactsAsync(account1);
        Assert.Single(account1ContactsFinal);
        Assert.Equal("dave@example.com", account1ContactsFinal[0].ContactJid);

        // Verify account2 contacts (Charlie still present for account2)
        var account2ContactsFinal = await repo.GetContactsAsync(account2);
        Assert.Single(account2ContactsFinal);
        Assert.Equal("charlie@example.com", account2ContactsFinal[0].ContactJid);
    }

    [Fact]
    public async Task RosterRepository_IndividualVsBatchUpserts_PerformanceComparison()
    {
        var repo = new RosterRepository(_context);
        string account = "alice@example.com";
        int count = 200;

        var contacts1 = Enumerable.Range(1, count).Select(i => new RosterContact
        {
            AccountJid = account,
            ContactJid = $"indiv_user{i}@example.com",
            Name = $"User {i}",
            Subscription = "both",
            Groups = "Friends"
        }).ToList();

        var swIndiv = System.Diagnostics.Stopwatch.StartNew();
        foreach (var c in contacts1)
        {
            await repo.UpsertContactAsync(c);
        }
        swIndiv.Stop();

        var contacts2 = Enumerable.Range(1, count).Select(i => new RosterContact
        {
            AccountJid = account,
            ContactJid = $"batch_user{i}@example.com",
            Name = $"Batch User {i}",
            Subscription = "both",
            Groups = "Friends"
        }).ToList();

        var swBatch = System.Diagnostics.Stopwatch.StartNew();
        await repo.UpsertContactsAsync(contacts2);
        swBatch.Stop();

        _output.WriteLine($"Individual upserts for {count} contacts took {swIndiv.ElapsedMilliseconds} ms");
        _output.WriteLine($"Batch upsert for {count} contacts took {swBatch.ElapsedMilliseconds} ms");

        var retrieved = await repo.GetContactsAsync(account);
        Assert.Equal(count * 2, retrieved.Count);
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
    public async Task MessageRepository_GetLatestMessageTimestampAndSummaries_WorkCorrectly()
    {
        var repo = new MessageRepository(_context);
        string account = "user@example.com";
        string remote1 = "bob@example.com";
        string remote2 = "carol@example.com";

        var t1 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t3 = DateTimeOffset.UtcNow.AddMinutes(-5);

        await repo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote1,
            SenderJid = remote1,
            Timestamp = t1,
            Direction = MessageDirection.Inbound,
            Body = "Hello from Bob",
            IsRead = true
        });

        await repo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote1,
            SenderJid = remote1,
            Timestamp = t2,
            Direction = MessageDirection.Inbound,
            Body = "Second msg from Bob",
            IsRead = false
        });

        await repo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote2,
            SenderJid = remote2,
            Timestamp = t3,
            Direction = MessageDirection.Inbound,
            Body = "Unread from Carol",
            IsRead = false
        });

        // 1. Get latest timestamp for account
        var latestAccount = await repo.GetLatestMessageTimestampAsync(account);
        Assert.NotNull(latestAccount);
        Assert.Equal(t3.ToUniversalTime().ToString("O"), latestAccount.Value.ToUniversalTime().ToString("O"));

        // 2. Get latest timestamp for remote1
        var latestBob = await repo.GetLatestMessageTimestampAsync(account, remote1);
        Assert.NotNull(latestBob);
        Assert.Equal(t2.ToUniversalTime().ToString("O"), latestBob.Value.ToUniversalTime().ToString("O"));

        // 3. Get contact summaries (unread count and last preview)
        var summaries = await repo.GetContactSummariesAsync(account);
        Assert.Equal(2, summaries.Count);
        Assert.True(summaries.ContainsKey(remote1));
        Assert.Equal(1, summaries[remote1].unreadCount);
        Assert.Equal("Second msg from Bob", summaries[remote1].lastPreview);

        Assert.True(summaries.ContainsKey(remote2));
        Assert.Equal(1, summaries[remote2].unreadCount);
        Assert.Equal("Unread from Carol", summaries[remote2].lastPreview);
    }

    [Fact]
    public void DatabaseContext_GetDefaultDatabasePath_ReturnsValidAppDataPath()
    {
        string defaultPath = DatabaseContext.GetDefaultDatabasePath();
        Assert.False(string.IsNullOrWhiteSpace(defaultPath));
        Assert.EndsWith("netchatx.db", defaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.GetDirectoryName(defaultPath)));
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}

