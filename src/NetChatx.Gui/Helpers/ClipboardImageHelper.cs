using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace NetChatx.Gui.Helpers;

public static class ClipboardImageHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico"
    };

    public static async Task<byte[]?> GetClipboardImageBytesAsync(TopLevel? topLevel)
    {
        if (topLevel?.Clipboard is null) return null;
        var clipboard = topLevel.Clipboard;

        try
        {
            var formats = await clipboard.GetFormatsAsync();

            // 1. Check for PNG or image formats directly in Avalonia clipboard
            foreach (var fmt in formats)
            {
                if (fmt.Equals("PNG", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("Bitmap", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase))
                {
                    var data = await clipboard.GetDataAsync(fmt);
                    if (data is byte[] bytes && bytes.Length > 0) return bytes;
                    if (data is Stream stream)
                    {
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        return ms.ToArray();
                    }
                    if (data is Avalonia.Media.Imaging.Bitmap avaloniaBmp)
                    {
                        using var ms = new MemoryStream();
                        avaloniaBmp.Save(ms);
                        return ms.ToArray();
                    }
                }
            }

            // 2. Check for copied image files (Files or FileNames format)
            foreach (var fmt in formats)
            {
                if (fmt.Equals(DataFormats.Files, StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals(DataFormats.FileNames, StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("Files", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("FileNames", StringComparison.OrdinalIgnoreCase))
                {
                    var data = await clipboard.GetDataAsync(fmt);
                    if (data is IEnumerable<IStorageItem> storageItems)
                    {
                        foreach (var item in storageItems)
                        {
                            var localPath = item.Path.LocalPath;
                            if (IsImageFile(localPath) && File.Exists(localPath))
                            {
                                return await File.ReadAllBytesAsync(localPath);
                            }
                        }
                    }
                    else if (data is IStorageItem singleItem)
                    {
                        var localPath = singleItem.Path.LocalPath;
                        if (IsImageFile(localPath) && File.Exists(localPath))
                        {
                            return await File.ReadAllBytesAsync(localPath);
                        }
                    }
                    else if (data is IEnumerable<string> filePaths)
                    {
                        foreach (var path in filePaths)
                        {
                            if (IsImageFile(path) && File.Exists(path))
                            {
                                return await File.ReadAllBytesAsync(path);
                            }
                        }
                    }
                    else if (data is string singlePath)
                    {
                        if (IsImageFile(singlePath) && File.Exists(singlePath))
                        {
                            return await File.ReadAllBytesAsync(singlePath);
                        }
                    }
                }
            }

            // 3. Fallback on Windows: Win32 native clipboard extraction (CF_DIB / PNG)
            if (OperatingSystem.IsWindows())
            {
                var win32Bytes = Win32ClipboardHelper.GetImageBytesFromClipboard();
                if (win32Bytes is not null && win32Bytes.Length > 0)
                {
                    return win32Bytes;
                }
            }
        }
        catch
        {
            // Soft failure on clipboard access
        }

        return null;
    }

    public static bool IsImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext);
    }
}
