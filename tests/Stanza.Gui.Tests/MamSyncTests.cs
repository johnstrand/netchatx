using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Gui.ViewModels;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class MamSyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;

    public MamSyncTests()
    {
        _dbPath = $"testmamsync_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
    }

    private sealed class MockMamManager : Xep0313MessageArchiveManagement
    {
        public int QueryCount { get; private set; }
        public int FailuresToSimulate { get; set; }

        private readonly List<List<MamMessageItem>> _pages;

        public MockMamManager(List<List<MamMessageItem>> pages)
        {
            _pages = pages;
        }

        public override Task<MamQueryResult> QueryArchiveAsync(
            Jid? withJid = null,
            Jid? archiveJid = null,
            int maxResults = 50,
            string? before = null,
            string? after = null,
            DateTimeOffset? start = null,
            DateTimeOffset? end = null,
            CancellationToken ct = default)
        {
            QueryCount++;

            if (FailuresToSimulate > 0)
            {
                FailuresToSimulate--;
                throw new InvalidOperationException("Simulated network failure");
            }

            var pageIdx = 0;
            if (after != null && int.TryParse(after, out int parsedAfter))
            {
                pageIdx = parsedAfter + 1;
            }
            else if (before != null && int.TryParse(before, out int parsedBefore))
            {
                pageIdx = parsedBefore - 1;
            }
            else if (before != null)
            {
                pageIdx = Math.Max(0, _pages.Count - 1);
            }

            if (pageIdx < 0 || pageIdx >= _pages.Count)
            {
                return Task.FromResult(new MamQueryResult
                {
                    Messages = Array.Empty<MamMessageItem>(),
                    IsComplete = true
                });
            }

            var pageItems = _pages[pageIdx];
            var isLast = pageIdx == _pages.Count - 1;

            return Task.FromResult(new MamQueryResult
            {
                Messages = pageItems,
                IsComplete = isLast,
                FirstId = pageIdx.ToString(),
                LastId = pageIdx.ToString(),
                Count = pageItems.Count
            });
        }
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchive_PagesThroughAllMessagesAndRetriesOnFailure()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("friend@test.org");

        // Seed initial message bubble so startTimestamp is set, testing RSM 'after' pagination
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "seed_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Seed message",
            Timestamp = DateTimeOffset.UtcNow.AddHours(-1),
            Direction = MessageDirection.Inbound
        });

        // Prepare 12 pages of messages (more than old maxPages=10 limit)
        var pages = new List<List<MamMessageItem>>();
        var totalMsgCount = 0;
        for (int p = 0; p < 12; p++)
        {
            var page = new List<MamMessageItem>();
            for (int m = 0; m < 5; m++)
            {
                totalMsgCount++;
                var id = $"archive_msg_{p}_{m}";
                page.Add(new MamMessageItem
                {
                    ArchiveId = id,
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-50 + totalMsgCount),
                    Message = new MessageStanza(to: Jid.Parse(account), from: remote)
                    {
                        Body = $"Message #{totalMsgCount}"
                    }
                });
            }
            pages.Add(page);
        }

        var mockMam = new MockMamManager(pages)
        {
            FailuresToSimulate = 1 // Will fail once, then retry and succeed
        };

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Friend",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: null,
            mamManager: mockMam);

        // LoadHistoryAsync fetches SQLite messages AND triggers MAM sync
        await conv.LoadHistoryAsync();

        // 12 pages + 1 failure retry = 13 queries
        Assert.Equal(13, mockMam.QueryCount);
        Assert.Equal(61, conv.Messages.Count); // 1 seed + 60 MAM messages
        Assert.False(conv.IsSyncing);
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchive_ContinuousPagination_Works()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "pass"
        }, transport);

        await client.ConnectAsync();

        var mam = new Xep0313MessageArchiveManagement();
        await mam.AttachAsync(client);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client,
            mamManager: mam);

        Assert.False(conv.IsSyncing);

        // When SyncArchiveAsync is called on an empty server archive, it finishes gracefully
        await conv.SyncArchiveAsync();

        Assert.False(conv.IsSyncing);
        Assert.Equal(0, conv.SyncFetchedCount);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchive_WhenCaughtUp_QueriesOnceAndDoesNotHammerServer()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("friend@test.org");

        // Seed initial message so startTimestamp is set
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "seed_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Seed message",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            Direction = MessageDirection.Inbound
        });

        // Mock MAM returns 0 messages when queried with start timestamp (client is up to date)
        // If the buggy fallback existed, it would trigger a second query without start
        var mockMam = new MockMamManager([]);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Friend",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: null,
            mamManager: mockMam);

        await conv.LoadHistoryAsync();

        // Exactly 1 MAM query should be executed (checking for messages after seed_1)
        // It must NOT hammer the server with fallback queries
        Assert.Equal(1, mockMam.QueryCount);
        Assert.Single(conv.Messages);
        Assert.False(conv.IsSyncing);
        Assert.Equal(0, conv.SyncFetchedCount);
    }

    [Fact]
    public async Task ChatConversationViewModel_EnsureHistoryLoadedAsync_ConcurrentCalls_LoadsOnce()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("friend@test.org");

        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "seed_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Seed message",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            Direction = MessageDirection.Inbound
        });

        var mockMam = new MockMamManager([]);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Friend",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: null,
            mamManager: mockMam);

        // Run 5 concurrent EnsureHistoryLoadedAsync calls
        var tasks = Enumerable.Range(0, 5).Select(_ => conv.EnsureHistoryLoadedAsync()).ToArray();
        await Task.WhenAll(tasks);

        Assert.True(conv.HasLoadedHistory);
        Assert.Single(conv.Messages);
        Assert.Equal(1, mockMam.QueryCount);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
