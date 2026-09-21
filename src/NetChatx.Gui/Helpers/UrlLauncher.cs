using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace NetChatx.Gui.Helpers;

public static class UrlLauncher
{
    public static bool OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var target = url.Trim();
        if (target.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            target = "https://" + target;
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps &&
            uri.Scheme != Uri.UriSchemeMailto &&
            !uri.Scheme.Equals("xmpp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var uriString = uri.AbsoluteUri;

        try
        {
            var psi = new ProcessStartInfo(uriString)
            {
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var escaped = uriString.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{escaped}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    return true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", uriString);
                    return true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", uriString);
                    return true;
                }
            }
            catch
            {
                // Soft failure
            }
        }

        return false;
    }

    public static async Task<bool> CopyToClipboardAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard is not null)
                {
                    await clipboard.SetTextAsync(text);
                    return true;
                }
            }
        }
        catch
        {
            // Soft failure
        }
        return false;
    }
}
