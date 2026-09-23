using Avalonia.Controls;

namespace Stanza.Gui.Services;

public interface INotificationService
{
    bool IsWindowActive { get; set; }
    bool IsFlashing { get; }
    event Action<bool>? WindowActiveChanged;
    string? LastNotificationTitle { get; }
    string? LastNotificationMessage { get; }
    int SystemNotificationCount { get; }
    void ShowSystemNotification(string title, string message);
    void FlashWindow();
    void StopFlashing();
    void AttachWindow(Window? window);
    void DetachWindow();
}
