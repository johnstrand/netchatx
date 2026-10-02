using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Gui.Services;
using Stanza.Gui.ViewModels;
using Stanza.MockServer;
using Stanza.Storage;
using Xunit;

namespace Stanza.Gui.Tests;

public class ReconnectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;

    public ReconnectionTests()
    {
        _dbPath = $"test_reconnect_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private sealed class ConstantRandom : Random
    {
        private readonly double _value;
        public ConstantRandom(double value) => _value = value;
        public override double NextDouble() => _value;
    }

    private sealed class ControllableTransport : IXmppTransport
    {
        private readonly LoopbackTransport _inner = new();
        public bool FailConnection { get; set; }
        public bool WasConnected { get; private set; }

        public PipeReader Input => _inner.Input;
        public PipeWriter Output => _inner.Output;
        public bool IsSecure => _inner.IsSecure;
        public LoopbackTransport Inner => _inner;

        public ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            if (FailConnection)
            {
                throw new IOException("Server unreachable (connection refused)");
            }
            WasConnected = true;
            return _inner.ConnectAsync(host, port, cancellationToken);
        }

        public ValueTask UpgradeToTlsAsync(string targetHost, CancellationToken cancellationToken = default) =>
            _inner.UpgradeToTlsAsync(targetHost, cancellationToken);

        public ValueTask CloseAsync()
        {
            if (WasConnected)
            {
                return _inner.CloseAsync();
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => CloseAsync();
    }

    [Fact]
    public void CalculateBackoffDelay_CalculatesExponentialBackoffWithinJitterBounds()
    {
        // Attempt 1: base = 2.0s
        // min jitter (factor 0.8) -> 1.6s
        var delayMin1 = MainChatViewModel.CalculateBackoffDelay(1, random: new ConstantRandom(0.0));
        Assert.Equal(1.6, delayMin1.TotalSeconds, 2);

        // max jitter (factor 1.2) -> 2.4s
        var delayMax1 = MainChatViewModel.CalculateBackoffDelay(1, random: new ConstantRandom(1.0));
        Assert.Equal(2.4, delayMax1.TotalSeconds, 2);

        // Attempt 2: base = 4.0s
        var delayMin2 = MainChatViewModel.CalculateBackoffDelay(2, random: new ConstantRandom(0.0));
        Assert.Equal(3.2, delayMin2.TotalSeconds, 2);
        var delayMax2 = MainChatViewModel.CalculateBackoffDelay(2, random: new ConstantRandom(1.0));
        Assert.Equal(4.8, delayMax2.TotalSeconds, 2);

        // Attempt 3: base = 8.0s
        var delayMid3 = MainChatViewModel.CalculateBackoffDelay(3, random: new ConstantRandom(0.5));
        Assert.Equal(8.0, delayMid3.TotalSeconds, 2);

        // Attempt 4: base = 16.0s
        var delayMid4 = MainChatViewModel.CalculateBackoffDelay(4, random: new ConstantRandom(0.5));
        Assert.Equal(16.0, delayMid4.TotalSeconds, 2);

        // Attempt 5: base = 32.0s
        var delayMid5 = MainChatViewModel.CalculateBackoffDelay(5, random: new ConstantRandom(0.5));
        Assert.Equal(32.0, delayMid5.TotalSeconds, 2);

        // Attempt 6+: capped at 60.0s
        var delayMax6 = MainChatViewModel.CalculateBackoffDelay(6, random: new ConstantRandom(1.0));
        Assert.True(delayMax6.TotalSeconds <= 60.0);

        var delayMid7 = MainChatViewModel.CalculateBackoffDelay(7, random: new ConstantRandom(0.5));
        Assert.Equal(60.0, delayMid7.TotalSeconds, 2);
    }

    [Fact]
    public async Task PerformReconnectAsync_SucceedsOnFirstAttempt_ResetsStateAndStatus()
    {
        var transport = new ControllableTransport();
        await using var server = new MockXmppServer(transport.Inner);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@test.org"),
            Password = "secret"
        }, transport);

        var mainVm = new MainChatViewModel(
            client,
            _dbContext,
            onDisconnectRequested: () => Task.CompletedTask,
            notificationService: new NotificationService(dispatchNative: false));

        mainVm.DelayProvider = (duration, token) => Task.CompletedTask;

        var result = await mainVm.ReconnectAsync();

        Assert.True(result);
        Assert.True(client.IsReady);
        Assert.False(mainVm.IsReconnecting);
        Assert.False(mainVm.CanManualReconnect);
        Assert.StartsWith("Connected as ", mainVm.StatusMessage);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task PerformReconnectAsync_RetriesWithBackoffAndCountdown_SucceedsOnLaterAttempt()
    {
        var transport = new ControllableTransport { FailConnection = true };
        await using var server = new MockXmppServer(transport.Inner);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@test.org"),
            Password = "secret"
        }, transport);

        var statusHistory = new List<string>();

        var mainVm = new MainChatViewModel(
            client,
            _dbContext,
            onDisconnectRequested: () => Task.CompletedTask,
            notificationService: new NotificationService(dispatchNative: false));

        mainVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainChatViewModel.StatusMessage))
            {
                statusHistory.Add(mainVm.StatusMessage);
            }
        };

        int delayCalls = 0;
        mainVm.DelayProvider = (duration, token) =>
        {
            delayCalls++;
            // Allow connection during retry delay so next attempt succeeds
            transport.FailConnection = false;
            return Task.CompletedTask;
        };

        var result = await mainVm.ReconnectAsync();

        Assert.True(result);
        Assert.True(client.IsReady);
        Assert.False(mainVm.IsReconnecting);
        Assert.False(mainVm.CanManualReconnect);
        Assert.StartsWith("Connected as ", mainVm.StatusMessage);
        Assert.True(delayCalls > 0);

        // Check that countdown status messages were recorded (e.g. attempt 2/10 in ...s)
        Assert.Contains(statusHistory, s => s.Contains("attempt 2/10 in"));

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task PerformReconnectAsync_AllAttemptsFail_UpdatesStatusMessageAndSetsCanManualReconnect()
    {
        var transport = new ControllableTransport { FailConnection = true };
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@test.org"),
            Password = "secret"
        }, transport);

        var statusHistory = new List<string>();

        var mainVm = new MainChatViewModel(
            client,
            _dbContext,
            onDisconnectRequested: () => Task.CompletedTask,
            notificationService: new NotificationService(dispatchNative: false));

        mainVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainChatViewModel.StatusMessage))
            {
                statusHistory.Add(mainVm.StatusMessage);
            }
        };

        mainVm.DelayProvider = (duration, token) => Task.CompletedTask;

        var result = await mainVm.ReconnectAsync();

        Assert.False(result);
        Assert.False(client.IsReady);
        Assert.False(mainVm.IsReconnecting);
        Assert.True(mainVm.CanManualReconnect);
        Assert.Contains("Connection failed", mainVm.StatusMessage);
        Assert.Contains("Reconnect manually", mainVm.StatusMessage);

        // Status history should include countdown messages across retries
        Assert.Contains(statusHistory, s => s.Contains("attempt 2/10 in"));
        Assert.Contains(statusHistory, s => s.Contains("attempt 10/10 in"));
    }

    [Fact]
    public async Task PerformReconnectAsync_WhenManualDisconnect_AbortsImmediatelyWithoutRetrying()
    {
        var transport = new ControllableTransport { FailConnection = true };
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@test.org"),
            Password = "secret"
        }, transport);

        var mainVm = new MainChatViewModel(
            client,
            _dbContext,
            onDisconnectRequested: () => Task.CompletedTask,
            notificationService: new NotificationService(dispatchNative: false));

        int delayCount = 0;
        mainVm.DelayProvider = async (duration, token) =>
        {
            delayCount++;
            // Trigger manual disconnect during retry delay
            await mainVm.DisconnectAsync();
        };

        var result = await mainVm.ReconnectAsync();

        Assert.False(result);
        Assert.False(mainVm.IsReconnecting);
        Assert.False(mainVm.CanManualReconnect);
        // It should have aborted quickly after disconnect, not completing all 10 attempts
        Assert.True(delayCount < 5);
    }

    [Fact]
    public async Task ManualReconnectCommand_ResetsStateAndTriggersReconnect()
    {
        var transport = new ControllableTransport { FailConnection = true };
        await using var server = new MockXmppServer(transport.Inner);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("user@test.org"),
            Password = "secret"
        }, transport);

        var mainVm = new MainChatViewModel(
            client,
            _dbContext,
            onDisconnectRequested: () => Task.CompletedTask,
            notificationService: new NotificationService(dispatchNative: false));

        mainVm.DelayProvider = (duration, token) => Task.CompletedTask;

        // Exhaust automatic reconnects while server unreachable
        await mainVm.ReconnectAsync();
        Assert.True(mainVm.CanManualReconnect);

        // Now allow transport connection
        transport.FailConnection = false;

        // Trigger manual reconnect command
        await mainVm.ManualReconnectCommand.ExecuteAsync(null);

        Assert.True(client.IsReady);
        Assert.False(mainVm.IsReconnecting);
        Assert.False(mainVm.CanManualReconnect);
        Assert.StartsWith("Connected as ", mainVm.StatusMessage);

        await client.DisconnectAsync();
    }
}
