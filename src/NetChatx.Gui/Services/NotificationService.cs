using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Tmds.DBus.Protocol;

namespace NetChatx.Gui.Services;

public sealed class NotificationService : INotificationService
{
    private Window? _window;
    private bool _isFlashing;
    private bool _isWindowActive = true;

    public event Action<bool>? WindowActiveChanged;

    public string? LastNotificationTitle { get; private set; }
    public string? LastNotificationMessage { get; private set; }
    public int SystemNotificationCount { get; private set; }

    public bool IsWindowActive
    {
        get
        {
            var win = GetWindow();
            return win is not null ? win.IsActive : _isWindowActive;
        }
        set
        {
            if (_isWindowActive != value)
            {
                _isWindowActive = value;
                WindowActiveChanged?.Invoke(value);
            }
        }
    }

    public bool IsFlashing => _isFlashing;

    private readonly bool _dispatchNative;

    private static bool DetectTestEnvironment()
    {
        try
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            {
                var name = a.GetName().Name;
                return name != null && (
                    name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
            }))
            {
                return true;
            }

            var procName = Process.GetCurrentProcess().ProcessName;
            if (procName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
                procName.Contains("vstest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
            // Soft failure
        }

        return false;
    }

    /// <summary>
    /// Gets or sets whether native OS notifications (PowerShell toasts, notify-send, AppleScript) are dispatched.
    /// Defaults to true in application execution, or false when running under automated tests.
    /// </summary>
    public static bool EnableNativeNotifications { get; set; } = !DetectTestEnvironment();

    public NotificationService(bool dispatchNative = true)
    {
        _dispatchNative = dispatchNative;
    }

    public void ShowSystemNotification(string title, string message)
    {
        LastNotificationTitle = title;
        LastNotificationMessage = message;
        SystemNotificationCount++;

        if (!_dispatchNative || !EnableNativeNotifications)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            ShowWindowsNotification(title, message);
        }
        else if (OperatingSystem.IsLinux())
        {
            ShowLinuxNotification(title, message);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ShowMacNotification(title, message);
        }
    }

    private bool _trayIconAdded;

    private void ShowWindowsNotification(string title, string message)
    {
        if (!EnableNativeNotifications) return;

        try
        {
            var handle = GetWindowHandle();
            if (handle != IntPtr.Zero)
            {
                var hIcon = SendMessage(handle, WM_GETICON, (IntPtr)1, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = SendMessage(handle, WM_GETICON, (IntPtr)0, IntPtr.Zero);
                }
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
                }
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = LoadIcon(IntPtr.Zero, IDI_INFORMATION);
                }

                var nid = new NOTIFYICONDATAW
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = handle,
                    uID = TRAY_NOTIFICATION_ID,
                    uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO,
                    uCallbackMessage = WM_USER + 1024,
                    hIcon = hIcon,
                    szTip = "NetChatx",
                    szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title,
                    szInfo = message.Length > 255 ? message.Substring(0, 255) : message,
                    dwInfoFlags = NIIF_INFO | NIIF_LARGE_ICON,
                    uTimeoutOrVersion = NOTIFYICON_VERSION_4
                };

                if (!_trayIconAdded)
                {
                    if (Shell_NotifyIconW(NIM_ADD, ref nid))
                    {
                        _trayIconAdded = true;
                        Shell_NotifyIconW(NIM_SETVERSION, ref nid);
                    }
                }

                var modified = Shell_NotifyIconW(NIM_MODIFY, ref nid);
                if (!modified && !_trayIconAdded)
                {
                    Shell_NotifyIconW(NIM_ADD, ref nid);
                    if (Shell_NotifyIconW(NIM_MODIFY, ref nid))
                    {
                        _trayIconAdded = true;
                    }
                }
            }
        }
        catch
        {
            // Soft failure on shell notify
        }

        // Also trigger PowerShell Toast using a temporary file to avoid CLI argument escaping issues
        ShowWindowsPowerShellToast(title, message);
    }

    private static void ShowWindowsPowerShellToast(string title, string message)
    {
        Task.Run(() =>
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"netchatx_toast_{Guid.NewGuid():N}.ps1");
            try
            {
                var safeTitle = (title ?? string.Empty).Replace("'", "''");
                var safeMessage = (message ?? string.Empty).Replace("'", "''");

                var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$template = @'
<toast>
  <visual>
    <binding template=""ToastGeneric"">
      <text>{safeTitle}</text>
      <text>{safeMessage}</text>
    </binding>
  </visual>
</toast>
'@
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml($template)
$toast = New-Object Windows.UI.Notifications.ToastNotification $xml
try {{
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{{1AC14E77-02E7-4E60-B74E-226C36329A3B}}\WindowsPowerShell\v1.0\powershell.exe').Show($toast)
}} catch {{
    try {{
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('NetChatx').Show($toast)
    }} catch {{
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel').Show($toast)
    }}
}}
";
                File.WriteAllText(tempFile, script, System.Text.Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{tempFile}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(4000);
            }
            catch
            {
                // Soft failure
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Soft failure
                }
            }
        });
    }

    private static void ShowLinuxNotification(string title, string message)
    {
        if (!EnableNativeNotifications) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "notify-send",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add("NetChatx");
            psi.ArgumentList.Add(title ?? string.Empty);
            psi.ArgumentList.Add(message ?? string.Empty);

            Task.Run(() =>
            {
                try
                {
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
                catch
                {
                    _ = SendDbusNotificationAsync(title ?? string.Empty, message ?? string.Empty);
                }
            });
        }
        catch
        {
            _ = SendDbusNotificationAsync(title ?? string.Empty, message ?? string.Empty);
        }
    }

    private static async Task SendDbusNotificationAsync(string title, string message)
    {
        try
        {
            if (string.IsNullOrEmpty(Address.Session)) return;

            using var connection = new Connection(Address.Session);
            await connection.ConnectAsync();

            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.Notifications",
                path: "/org/freedesktop/Notifications",
                @interface: "org.freedesktop.Notifications",
                member: "Notify",
                signature: "susssasa{sv}i");

            writer.WriteString("NetChatx");
            writer.WriteUInt32(0);
            writer.WriteString("netchatx");
            writer.WriteString(title);
            writer.WriteString(message);

            var actionsWriter = writer.WriteArrayStart(DBusType.String);
            writer.WriteArrayEnd(actionsWriter);

            var hintsWriter = writer.WriteDictionaryStart();
            writer.WriteDictionaryEnd(hintsWriter);

            writer.WriteInt32(5000);

            var msg = writer.CreateMessage();
            await connection.CallMethodAsync(msg);
        }
        catch
        {
            // Soft failure
        }
    }

    private static void ShowMacNotification(string title, string message)
    {
        if (!EnableNativeNotifications) return;

        try
        {
            var safeTitle = EscapeAppleScript(title);
            var safeMessage = EscapeAppleScript(message);
            var script = $"display notification \"{safeMessage}\" with title \"NetChatx\" subtitle \"{safeTitle}\" sound name \"default\"";

            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Task.Run(() =>
            {
                try
                {
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
                catch
                {
                    // Soft failure
                }
            });
        }
        catch
        {
            // Soft failure
        }
    }

    private static string EscapeAppleScript(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public Window? GetWindow()
    {
        if (_window is not null) return _window;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is not null)
            {
                AttachWindow(desktop.MainWindow);
                return desktop.MainWindow;
            }
        }

        return null;
    }

    public IntPtr GetWindowHandle()
    {
        var win = GetWindow();
        return win?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }

    public void AttachWindow(Window? window)
    {
        if (_window == window && _window is not null) return;

        DetachWindow();

        _window = window;
        if (_window is not null)
        {
            _isWindowActive = _window.IsActive;
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
            WindowActiveChanged?.Invoke(_isWindowActive);
        }
    }

    public void DetachWindow()
    {
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;

            if (_trayIconAdded)
            {
                try
                {
                    var handle = GetWindowHandle();
                    if (handle != IntPtr.Zero)
                    {
                        var nid = new NOTIFYICONDATAW
                        {
                            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                            hWnd = handle,
                            uID = TRAY_NOTIFICATION_ID
                        };
                        Shell_NotifyIconW(NIM_DELETE, ref nid);
                    }
                }
                catch
                {
                    // Soft failure
                }
                _trayIconAdded = false;
            }

            _window = null;
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _isWindowActive = true;
        WindowActiveChanged?.Invoke(true);
        StopFlashing();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _isWindowActive = false;
        WindowActiveChanged?.Invoke(false);
    }

    public void FlashWindow()
    {
        if (OperatingSystem.IsWindows() && EnableNativeNotifications)
        {
            try
            {
                var handle = GetWindowHandle();
                if (handle != IntPtr.Zero)
                {
                    var info = new FLASHWINFO
                    {
                        cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                        hwnd = handle,
                        dwFlags = FLASHW_ALL | FLASHW_TIMER,
                        uCount = uint.MaxValue,
                        dwTimeout = 0
                    };
                    FlashWindowEx(ref info);
                    FlashWindow(handle, true);
                }
            }
            catch
            {
                // Soft failure if platform handle or P/Invoke is not supported
            }
        }

        _isFlashing = true;
    }

    public void StopFlashing()
    {
        if (OperatingSystem.IsWindows() && EnableNativeNotifications)
        {
            try
            {
                var handle = GetWindowHandle();
                if (handle != IntPtr.Zero)
                {
                    var info = new FLASHWINFO
                    {
                        cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                        hwnd = handle,
                        dwFlags = FLASHW_STOP,
                        uCount = 0,
                        dwTimeout = 0
                    };
                    FlashWindowEx(ref info);
                    FlashWindow(handle, false);
                }
            }
            catch
            {
                // Soft failure if platform handle or P/Invoke is not supported
            }
        }

        _isFlashing = false;
    }

    #region Win32 P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_STOP = 0;
    private const uint FLASHW_CAPTION = 1;
    private const uint FLASHW_TRAY = 2;
    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMER = 4;
    private const uint FLASHW_TIMERNOFG = 12;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_NONE = 0x00000000;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_WARNING = 0x00000002;
    private const uint NIIF_ERROR = 0x00000003;
    private const uint NIIF_LARGE_ICON = 0x00000020;

    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint WM_USER = 0x0400;
    private const uint TRAY_NOTIFICATION_ID = 1001;
    private const uint WM_GETICON = 0x007F;
    private const IntPtr IDI_APPLICATION = 32512;
    private const IntPtr IDI_INFORMATION = 32516;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    #endregion
}
