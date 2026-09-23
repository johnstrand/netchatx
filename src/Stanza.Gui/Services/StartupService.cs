using System;
using System.IO;
using System.Runtime.Versioning;

namespace Stanza.Gui.Services;

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

        var baseDir = AppContext.BaseDirectory;
        if (OperatingSystem.IsWindows())
        {
            var stanzaExe = Path.Combine(baseDir, "Stanza.exe");
            if (File.Exists(stanzaExe))
            {
                return stanzaExe;
            }
        }
        else
        {
            var stanzaBin = Path.Combine(baseDir, "Stanza");
            if (File.Exists(stanzaBin))
            {
                return stanzaBin;
            }
        }

        return Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "Stanza";
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
        // 1. Check HKCU Run first
        using var hkcuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        var val = hkcuKey?.GetValue("Stanza") as string;
        if (!string.IsNullOrWhiteSpace(val))
        {
            return IsStartupApproved("Stanza");
        }

        // 2. Check HKLM Run (e.g. machine-wide or installer entry)
        using var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        var hklmVal = hklmKey?.GetValue("Stanza") as string;
        if (!string.IsNullOrWhiteSpace(hklmVal))
        {
            return IsStartupApproved("Stanza");
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsStartupApproved(string valueName)
    {
        try
        {
            using var approvedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", false);
            if (approvedKey?.GetValue(valueName) is byte[] bytes && bytes.Length > 0)
            {
                // In Windows StartupApproved:
                // An even number / 0x02 indicates enabled.
                // An odd number / 0x01 or 0x03 indicates disabled by user in Task Manager.
                return (bytes[0] & 1) == 0;
            }
        }
        catch
        {
            // Fallback gracefully on read errors
        }
        return true;
    }

    [SupportedOSPlatform("windows")]
    private bool SetWindowsStartupEnabled(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key is null) return false;

        if (enable)
        {
            var exePath = GetExecutablePath();
            key.SetValue("Stanza", $"\"{exePath}\"");

            // Ensure not marked as disabled in StartupApproved
            try
            {
                using var approvedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
                if (approvedKey?.GetValue("Stanza") is byte[] bytes && bytes.Length > 0)
                {
                    bytes[0] = 0x02;
                    approvedKey.SetValue("Stanza", bytes, Microsoft.Win32.RegistryValueKind.Binary);
                }
            }
            catch
            {
                // Best-effort
            }
        }
        else
        {
            key.DeleteValue("Stanza", false);

            try
            {
                using var approvedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
                approvedKey?.DeleteValue("Stanza", false);
            }
            catch
            {
                // Best-effort
            }
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
        return Path.Combine(homeDir, ".config", "autostart", "stanza.desktop");
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
                Name=Stanza
                Comment=Modern cross-platform XMPP/Jabber client
                Exec="{exePath}" %u
                Terminal=false
                X-GNOME-Autostart-enabled=true
                StartupWMClass=Stanza
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
        return Path.Combine(homeDir, "Library", "LaunchAgents", "com.stanza.desktop.plist");
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
                    <string>com.stanza.desktop</string>
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
