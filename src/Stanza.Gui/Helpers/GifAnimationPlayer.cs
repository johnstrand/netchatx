using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Stanza.Gui.Helpers;

public sealed class GifAnimationPlayer : IDisposable
{
    private readonly IReadOnlyList<(Bitmap Bitmap, int DurationMs)> _frames;
    private readonly Action<Bitmap> _onFrameUpdate;
    private Timer? _timer;
    private int _currentFrameIndex;
    private bool _disposed;

    public GifAnimationPlayer(IReadOnlyList<(Bitmap Bitmap, int DurationMs)> frames, Action<Bitmap> onFrameUpdate)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _onFrameUpdate = onFrameUpdate ?? throw new ArgumentNullException(nameof(onFrameUpdate));

        if (_frames.Count > 1)
        {
            var initialDelay = Math.Max(20, _frames[0].DurationMs);
            _timer = new Timer(OnTimerTick, null, initialDelay, Timeout.Infinite);
        }
    }

    private void OnTimerTick(object? state)
    {
        if (_disposed || _frames.Count <= 1) return;

        _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Count;
        var (bitmap, durationMs) = _frames[_currentFrameIndex];

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                _onFrameUpdate(bitmap);
            }
        });

        var nextDelay = Math.Max(20, durationMs);
        try
        {
            _timer?.Change(nextDelay, Timeout.Infinite);
        }
        catch
        {
            // Disposed race protection
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
