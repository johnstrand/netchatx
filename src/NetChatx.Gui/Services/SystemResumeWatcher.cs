using System;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NetChatx.Gui.Services;

public sealed class SystemResumeWatcher : ISystemResumeWatcher
{
    public event Func<Task>? Resumed;

    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _debounceInterval;
    private DateTimeOffset _lastTriggerTime = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.UtcNow;
    private readonly Task? _heartbeatTask;

    public SystemResumeWatcher(TimeSpan? debounceInterval = null, bool startHeartbeat = true)
    {
        _debounceInterval = debounceInterval ?? TimeSpan.FromSeconds(2);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch
            {
                // Soft failure if power events cannot be hooked
            }
        }

        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch
        {
            // Soft failure if network events cannot be hooked
        }

        if (startHeartbeat)
        {
            _heartbeatTask = Task.Run(RunHeartbeatLoopAsync);
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            TriggerResume();
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            TriggerResume();
        }
    }

    private async Task RunHeartbeatLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(_cts.Token))
                    break;

                var now = DateTimeOffset.UtcNow;
                var elapsed = now - _lastHeartbeat;
                _lastHeartbeat = now;

                // If elapsed time is significantly larger than the timer tick (>= 15s instead of 5s),
                // the machine was asleep / suspended and has just resumed.
                if (elapsed >= TimeSpan.FromSeconds(15))
                {
                    TriggerResume();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue heartbeat
            }
        }
    }

    public void TriggerResume()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastTriggerTime < _debounceInterval)
            {
                return;
            }
            _lastTriggerTime = now;
        }

        _ = Task.Run(async () =>
        {
            // Delay slightly to allow network stack and sockets to settle
            await Task.Delay(500);

            try
            {
                if (Resumed is not null)
                {
                    await Resumed.Invoke();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error invoking Resumed event: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch { }
        }

        try
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
        catch { }
    }
}
