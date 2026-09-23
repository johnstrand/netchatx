using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Stanza.Gui.Helpers;

public sealed record ClipboardImageResult(byte[] Bytes, string FileName, string ContentType, bool IsGif);

public static class ClipboardImageHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico"
    };

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static bool IsSupportedImageFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    public static async Task<byte[]?> GetClipboardImageBytesAsync(TopLevel? topLevel)
    {
        var result = await GetClipboardImageAsync(topLevel);
        return result?.Bytes;
    }

    public static async Task<ClipboardImageResult?> GetClipboardImageAsync(TopLevel? topLevel)
    {
        if (topLevel?.Clipboard is null) return null;
        var clipboard = topLevel.Clipboard;

        try
        {
            var formats = await clipboard.GetFormatsAsync();

            // 1. Check for HTML Format (used by Windows Emoji Keyboard GIFs, web browsers)
            foreach (var fmt in formats)
            {
                if (fmt.Equals("HTML Format", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("HTML", StringComparison.OrdinalIgnoreCase))
                {
                    string? html = null;
                    var data = await clipboard.GetDataAsync(fmt);
                    if (data is string strData) html = strData;
                    else if (data is byte[] byteData) html = System.Text.Encoding.UTF8.GetString(byteData);
                    else if (data is Stream stream)
                    {
                        using var reader = new StreamReader(stream);
                        html = await reader.ReadToEndAsync();
                    }

                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        var result = await ProcessHtmlImageAsync(html);
                        if (result is not null) return result;
                    }
                }
            }

            // 1b. Windows native fallback for HTML format (if Avalonia clipboard doesn't report it)
            if (OperatingSystem.IsWindows())
            {
                var win32Html = Win32ClipboardHelper.GetHtmlFromClipboard();
                if (!string.IsNullOrWhiteSpace(win32Html))
                {
                    var result = await ProcessHtmlImageAsync(win32Html);
                    if (result is not null) return result;
                }

                // Check native Win32 GIF formats (CF_GIF / image/gif)
                var win32GifBytes = Win32ClipboardHelper.GetGifBytesFromClipboard();
                if (win32GifBytes is not null && win32GifBytes.Length > 0)
                {
                    return new ClipboardImageResult(win32GifBytes, $"gif_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.gif", "image/gif", true);
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
                                var bytes = await File.ReadAllBytesAsync(localPath);
                                var isGif = GifDecoder.IsGif(bytes) || localPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                                var mime = isGif ? "image/gif" : GetMimeType(Path.GetExtension(localPath));
                                return new ClipboardImageResult(bytes, Path.GetFileName(localPath), mime, isGif);
                            }
                        }
                    }
                    else if (data is IStorageItem singleItem)
                    {
                        var localPath = singleItem.Path.LocalPath;
                        if (IsImageFile(localPath) && File.Exists(localPath))
                        {
                            var bytes = await File.ReadAllBytesAsync(localPath);
                            var isGif = GifDecoder.IsGif(bytes) || localPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                            var mime = isGif ? "image/gif" : GetMimeType(Path.GetExtension(localPath));
                            return new ClipboardImageResult(bytes, Path.GetFileName(localPath), mime, isGif);
                        }
                    }
                    else if (data is IEnumerable<string> filePaths)
                    {
                        foreach (var path in filePaths)
                        {
                            if (IsImageFile(path) && File.Exists(path))
                            {
                                var bytes = await File.ReadAllBytesAsync(path);
                                var isGif = GifDecoder.IsGif(bytes) || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                                var mime = isGif ? "image/gif" : GetMimeType(Path.GetExtension(path));
                                return new ClipboardImageResult(bytes, Path.GetFileName(path), mime, isGif);
                            }
                        }
                    }
                    else if (data is string singlePath)
                    {
                        if (IsImageFile(singlePath) && File.Exists(singlePath))
                        {
                            var bytes = await File.ReadAllBytesAsync(singlePath);
                            var isGif = GifDecoder.IsGif(bytes) || singlePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                            var mime = isGif ? "image/gif" : GetMimeType(Path.GetExtension(singlePath));
                            return new ClipboardImageResult(bytes, Path.GetFileName(singlePath), mime, isGif);
                        }
                    }
                }
            }

            // 3. Check for direct GIF/Image URL in plain text format
            var text = await clipboard.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var trimmed = text.Trim();
                if (IsImageUrl(trimmed))
                {
                    var fetchedBytes = await FetchImageBytesAsync(trimmed);
                    if (fetchedBytes is not null && fetchedBytes.Length > 0)
                    {
                        var isGif = GifDecoder.IsGif(fetchedBytes) || trimmed.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("tenor.com", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("giphy.com", StringComparison.OrdinalIgnoreCase);
                        var ext = isGif ? "gif" : "png";
                        var mime = isGif ? "image/gif" : "image/png";
                        var fileName = $"{(isGif ? "gif" : "image")}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{ext}";
                        return new ClipboardImageResult(fetchedBytes, fileName, mime, isGif);
                    }
                }
            }

            // 4. Check for PNG or image formats directly in Avalonia clipboard
            foreach (var fmt in formats)
            {
                if (fmt.Equals("image/gif", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("GIF", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("PNG", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("Bitmap", StringComparison.OrdinalIgnoreCase) ||
                    fmt.Equals("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase))
                {
                    var data = await clipboard.GetDataAsync(fmt);
                    byte[]? bytes = null;
                    if (data is byte[] byteArr && byteArr.Length > 0) bytes = byteArr;
                    else if (data is Stream stream)
                    {
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        bytes = ms.ToArray();
                    }
                    else if (data is Avalonia.Media.Imaging.Bitmap avaloniaBmp)
                    {
                        using var ms = new MemoryStream();
                        avaloniaBmp.Save(ms);
                        bytes = ms.ToArray();
                    }

                    if (bytes is not null && bytes.Length > 0)
                    {
                        var isGif = GifDecoder.IsGif(bytes) || fmt.Contains("gif", StringComparison.OrdinalIgnoreCase);
                        var ext = isGif ? "gif" : "png";
                        var mime = isGif ? "image/gif" : "image/png";
                        var fileName = $"{(isGif ? "gif" : "image")}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{ext}";
                        return new ClipboardImageResult(bytes, fileName, mime, isGif);
                    }
                }
            }

            // 5. Fallback on Windows: Win32 native clipboard extraction (CF_DIB / PNG)
            if (OperatingSystem.IsWindows())
            {
                var win32Bytes = Win32ClipboardHelper.GetImageBytesFromClipboard();
                if (win32Bytes is not null && win32Bytes.Length > 0)
                {
                    var isGif = GifDecoder.IsGif(win32Bytes);
                    var ext = isGif ? "gif" : "png";
                    var mime = isGif ? "image/gif" : "image/png";
                    var fileName = $"{(isGif ? "gif" : "image")}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{ext}";
                    return new ClipboardImageResult(win32Bytes, fileName, mime, isGif);
                }
            }
        }
        catch
        {
            // Soft failure on clipboard access
        }

        return null;
    }

    public static string? ExtractImageSourceFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var match = Regex.Match(
            html,
            @"<img\b[^>]*?\bsrc\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var url = match.Groups[1].Success ? match.Groups[1].Value
                       : match.Groups[2].Success ? match.Groups[2].Value
                       : match.Groups[3].Value;

            return System.Net.WebUtility.HtmlDecode(url.Trim());
        }

        return null;
    }

    private static async Task<ClipboardImageResult?> ProcessHtmlImageAsync(string html)
    {
        var srcUrl = ExtractImageSourceFromHtml(html);
        if (string.IsNullOrWhiteSpace(srcUrl)) return null;

        var bytes = await FetchImageBytesAsync(srcUrl);
        if (bytes is not null && bytes.Length > 0)
        {
            var isGif = GifDecoder.IsGif(bytes) ||
                         srcUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                         srcUrl.Contains("tenor.com", StringComparison.OrdinalIgnoreCase) ||
                         srcUrl.Contains("giphy.com", StringComparison.OrdinalIgnoreCase);

            var ext = isGif ? "gif" : "png";
            var mime = isGif ? "image/gif" : "image/png";
            var fileName = $"{(isGif ? "gif" : "image")}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{ext}";
            return new ClipboardImageResult(bytes, fileName, mime, isGif);
        }

        return null;
    }

    public static async Task<byte[]?> FetchImageBytesAsync(string url, bool allowHttp = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return await HttpClient.GetByteArrayAsync(url, ct);
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowHttp) return null;
                return await HttpClient.GetByteArrayAsync(url, ct);
            }

            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var commaIdx = url.IndexOf(',');
                if (commaIdx > 0)
                {
                    return Convert.FromBase64String(url[(commaIdx + 1)..]);
                }
            }
        }
        catch
        {
            // Soft failure
        }

        return null;
    }

    public static bool IsImageUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Contains("tenor.com", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("giphy.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var ext in ImageExtensions)
        {
            if (text.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public static bool IsImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext);
    }

    public static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".gif" => "image/gif",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
    }
}
