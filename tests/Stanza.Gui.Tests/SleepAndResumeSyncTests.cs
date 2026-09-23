using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Gui.Services;
using Stanza.Gui.ViewModels;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class SleepAndResumeSyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;

    public SleepAndResumeSyncTests()
    {
        _dbPath = $"test_sleep_sync_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
    }

    private sealed class MockResumeWatcher : ISystemResumeWatcher
    {
        public event Func<Task>? Resumed;
        public int TriggerCount { get; private set; }

        public void TriggerResume()
        {
            TriggerCount++;
            _ = Resumed?.Invoke();
        }

        public void Dispose() { }
    }

    private sealed class MockMamManager : Xep0313MessageArchiveManagement
    {
        public int QueryCount { get; private set; }
        private readonly List<MamMessageItem> _messages;

        public MockMamManager(List<MamMessageItem> messages)
        {
            _messages = messages;
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
            return Task.FromResult(new MamQueryResult
            {
                Messages = _messages,
                IsComplete = true,
                FirstId = _messages.Count > 0 ? _messages[0].ArchiveId : null,
                LastId = _messages.Count > 0 ? _messages[^1].ArchiveId : null,
                Count = _messages.Count
            });
        }
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchive_AwaitsEnsureConnected_BeforeSyncing()
    {
        string account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");
        var mamMessages = new List<MamMessageItem>
        {
            new()
            {
                ArchiveId = "m1",
                Timestamp = DateTimeOffset.UtcNow,
                Message = new MessageStanza(to: Jid.Parse(account), from: remote) { Body = "Backlog message 1" }
            }
        };

        var mockMam = new MockMamManager(mamMessages);
        bool ensureConnectedCalled = false;

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: null,
            mamManager: mockMam,
            ensureConnected: () =>
            {
                ensureConnectedCalled = true;
                return Task.FromResult(true);
            });

        await conv.SyncArchiveAsync();

        Assert.True(ensureConnectedCalled);
        Assert.Equal(1, mockMam.QueryCount);
        Assert.Single(conv.Messages);
        Assert.Equal("Backlog message 1", conv.Messages[0].Body);
        Assert.False(conv.IsSyncing);
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchive_WhenEnsureConnectedFails_GracefullyAbortsWithoutQuerying()
    {
        string account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var mockMam = new MockMamManager([]);
        bool ensureConnectedCalled = false;

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: null,
            mamManager: mockMam,
            ensureConnected: () =>
            {
                ensureConnectedCalled = true;
                return Task.FromResult(false); // e.g. reconnection failed (offline)
            });

        await conv.SyncArchiveAsync();

        Assert.True(ensureConnectedCalled);
        Assert.Equal(0, mockMam.QueryCount);
        Assert.False(conv.IsSyncing);
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchive_WhenClientNotReadyAndNoDelegate_Aborts()
    {
        string account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var mockMam = new MockMamManager([]);
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "pass"
        }, new LoopbackTransport());

        // Client is in Disconnected state (not ready)
        Assert.False(client.IsReady);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client,
            mamManager: mockMam);

        await conv.SyncArchiveAsync();

        Assert.Equal(0, mockMam.QueryCount);
        Assert.False(conv.IsSyncing);
    }

    [Fact]
    public async Task XmppClient_SendIqAsync_ThrowsImmediatelyWhenDisconnected()
    {
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@test.org"),
            Password = "password"
        }, new LoopbackTransport());

        Assert.Equal(XmppClientState.Disconnected, client.State);
        Assert.False(client.IsConnected);
        Assert.False(client.IsReady);

        var iq = IqStanza.CreateGet();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendIqAsync(iq, timeout: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task MainChatViewModel_SystemResumeWatcher_TriggersResumeHandler()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@test.org"),
            Password = "secret"
        }, transport);

        await client.ConnectAsync();
        Assert.True(client.IsReady);

        var mockWatcher = new MockResumeWatcher();
        var mainVm = new MainChatViewModel(
            client,
            _dbContext,
            onDisconnectRequested: () => Task.CompletedTask,
            notificationService: new NotificationService(dispatchNative: false),
            resumeWatcher: mockWatcher);

        await mainVm.InitializeAsync();

        // Simulate resume from sleep
        mockWatcher.TriggerResume();

        // Give the async handler a moment to run
        await Task.Delay(200);

        Assert.True(client.IsReady);
        Assert.Equal(1, mockWatcher.TriggerCount);

        await client.DisconnectAsync();
    }

    [Fact]
    public void SystemResumeWatcher_TriggerResume_DebouncesQuickSuccessiveCalls()
    {
        var watcher = new SystemResumeWatcher(debounceInterval: TimeSpan.FromMilliseconds(500), startHeartbeat: false);
        int invokedCount = 0;
        watcher.Resumed += () =>
        {
            Interlocked.Increment(ref invokedCount);
            return Task.CompletedTask;
        };

        // Fire 5 times rapidly
        for (int i = 0; i < 5; i++)
        {
            watcher.TriggerResume();
        }

        // Wait for debounce delay and dispatch
        Thread.Sleep(750);

        Assert.Equal(1, invokedCount);
        watcher.Dispose();
    }

    [Fact]
    public async Task MainChatViewModel_CatchUpAccountArchive_WhenCaughtUp_DoesNotHammerServer()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "msg_seed_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Seed message",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            Direction = MessageDirection.Inbound
        });

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "secret"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(
            client,
            _dbContext,
            onDisconnectRequested: () => Task.CompletedTask,
            notificationService: new NotificationService(dispatchNative: false));

        await mainVm.CatchUpAccountArchiveAsync();

        Assert.False(mainVm.IsAccountSyncing);
        Assert.Equal(string.Empty, mainVm.SyncStatusMessage);

        await client.DisconnectAsync();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
