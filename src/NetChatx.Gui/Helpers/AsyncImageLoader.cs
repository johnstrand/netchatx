using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace NetChatx.Gui.Helpers;

public static class AsyncImageLoader
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<Bitmap?> LoadImageAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        url = url.Trim();

        if (Cache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        try
        {
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var fileUri) && File.Exists(fileUri.LocalPath))
                {
                    await using var fs = File.OpenRead(fileUri.LocalPath);
                    var bitmap = new Bitmap(fs);
                    Cache[url] = bitmap;
                    return bitmap;
                }
            }
            else if (File.Exists(url))
            {
                await using var fs = File.OpenRead(url);
                var bitmap = new Bitmap(fs);
                Cache[url] = bitmap;
                return bitmap;
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await HttpClient.GetByteArrayAsync(url, ct);
                using var ms = new MemoryStream(bytes);
                var bitmap = new Bitmap(ms);
                Cache[url] = bitmap;
                return bitmap;
            }
        }
        catch
        {
            // Soft failure on image decode or network error
        }

        return null;
    }
}
