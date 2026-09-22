using System;
using System.IO;
using System.Runtime.Versioning;

namespace NetChatx.Gui.Services;

public class StartupService : IStartupService
{
    private readonly string? _customExePath;
    private readonly string? _customAutostartPath;

    public StartupService(string? customExePath = null, string? customAutostartPath = null)
    {
        _customExePath = customExePath;
        _customAutostartPath = customAutostartPath;
    }

    private string GetExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_customExePath))
        {
            return _customExePath;
        }

        return Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "NetChatx.Gui";
    }

    public bool IsStartupEnabled()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_customAutostartPath))
            {
                return IsLinuxStartupEnabled();
            }
            if (OperatingSystem.IsWindows())
            {
                return IsWindowsStartupEnabled();
            }
            if (OperatingSystem.IsLinux())
            {
                return IsLinuxStartupEnabled();
            }
            if (OperatingSystem.IsMacOS())
            {
                return IsMacStartupEnabled();
            }
        }
        catch
        {
            // Fallback gracefully on permission or system error
        }

        return false;
    }

    public bool SetStartupEnabled(bool enable)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_customAutostartPath))
            {
                return SetLinuxStartupEnabled(enable);
            }
            if (OperatingSystem.IsWindows())
            {
                return SetWindowsStartupEnabled(enable);
            }
            if (OperatingSystem.IsLinux())
            {
                return SetLinuxStartupEnabled(enable);
            }
            if (OperatingSystem.IsMacOS())
            {
                return SetMacStartupEnabled(enable);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private bool IsWindowsStartupEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        var val = key?.GetValue("NetChatx") as string;
        return !string.IsNullOrWhiteSpace(val);
    }

    [SupportedOSPlatform("windows")]
    private bool SetWindowsStartupEnabled(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key is null) return false;

        if (enable)
        {
            var exePath = GetExecutablePath();
            key.SetValue("NetChatx", $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue("NetChatx", false);
        }

        return true;
    }

    private string GetLinuxAutostartFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_customAutostartPath))
        {
            return _customAutostartPath;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "autostart", "netchatx.desktop");
    }

    private bool IsLinuxStartupEnabled()
    {
        var filePath = GetLinuxAutostartFilePath();
        return File.Exists(filePath);
    }

    private bool SetLinuxStartupEnabled(bool enable)
    {
        var filePath = GetLinuxAutostartFilePath();

        if (enable)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var exePath = GetExecutablePath();
            var content = $"""
                [Desktop Entry]
                Type=Application
                Name=NetChatx
                Comment=Modern cross-platform XMPP/Jabber client
                Exec="{exePath}" %u
                Terminal=false
                X-GNOME-Autostart-enabled=true
                StartupWMClass=NetChatx.Gui
                """;

            File.WriteAllText(filePath, content);
        }
        else
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        return true;
    }

    private string GetMacLaunchAgentFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_customAutostartPath))
        {
            return _customAutostartPath;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, "Library", "LaunchAgents", "com.netchatx.app.plist");
    }

    private bool IsMacStartupEnabled()
    {
        var filePath = GetMacLaunchAgentFilePath();
        return File.Exists(filePath);
    }

    private bool SetMacStartupEnabled(bool enable)
    {
        var filePath = GetMacLaunchAgentFilePath();

        if (enable)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var exePath = GetExecutablePath();
            var content = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>com.netchatx.app</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """;

            File.WriteAllText(filePath, content);
        }
        else
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        return true;
    }
}
