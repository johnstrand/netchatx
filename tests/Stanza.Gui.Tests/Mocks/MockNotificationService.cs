using System;
using Avalonia.Controls;
using Stanza.Gui.Services;

namespace Stanza.Gui.Tests.Mocks;

public class MockNotificationService : INotificationService
{
    private bool _isWindowActive = true;
    public event Action<bool>? WindowActiveChanged;

    public string? LastNotificationTitle { get; private set; }
    public string? LastNotificationMessage { get; private set; }
    public int SystemNotificationCount { get; private set; }

    public bool IsWindowActive
    {
        get => _isWindowActive;
        set
        {
            if (_isWindowActive != value)
            {
                _isWindowActive = value;
                WindowActiveChanged?.Invoke(value);
            }
        }
    }

    public bool IsFlashing { get; private set; }

    public void ShowSystemNotification(string title, string message)
    {
        LastNotificationTitle = title;
        LastNotificationMessage = message;
        SystemNotificationCount++;
    }

    public void FlashWindow()
    {
        IsFlashing = true;
    }

    public void StopFlashing()
    {
        IsFlashing = false;
    }

    public void AttachWindow(Window? window) { }
    public void DetachWindow() { }
}
