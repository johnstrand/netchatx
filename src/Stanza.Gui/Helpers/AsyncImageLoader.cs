using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Stanza.Gui.Helpers;

public static class AsyncImageLoader
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, (Bitmap Bitmap, List<(Bitmap Bitmap, int DurationMs)>? GifFrames)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<Bitmap?> LoadImageAsync(string url, CancellationToken ct = default)
    {
        var (bitmap, _) = await LoadImageOrGifAsync(url, ct);
        return bitmap;
    }

    public static async Task<(Bitmap? Bitmap, List<(Bitmap Bitmap, int DurationMs)>? GifFrames)> LoadImageOrGifAsync(
        string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return (null, null);

        url = url.Trim();

        if (Cache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        try
        {
            byte[]? bytes = null;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                bytes = await HttpClient.GetByteArrayAsync(url, ct);
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
            {
                if (File.Exists(fileUri.LocalPath))
                {
                    bytes = await File.ReadAllBytesAsync(fileUri.LocalPath, ct);
                }
            }
            else if (File.Exists(url))
            {
                bytes = await File.ReadAllBytesAsync(url, ct);
            }

            if (bytes is not null && bytes.Length > 0)
            {
                using var ms = new MemoryStream(bytes);
                var bitmap = new Bitmap(ms);

                List<(Bitmap Bitmap, int DurationMs)>? gifFrames = null;
                if (GifDecoder.IsGif(bytes))
                {
                    gifFrames = GifDecoder.DecodeFrames(bytes);
                }

                var entry = (bitmap, gifFrames);
                Cache[url] = entry;
                return entry;
            }
        }
        catch
        {
            // Soft failure on image decode or network error
        }

        return (null, null);
    }
}
