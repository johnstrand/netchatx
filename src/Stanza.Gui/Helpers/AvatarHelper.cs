using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Stanza.Gui.Helpers;

public static class AvatarHelper
{
    private static readonly string[] PaletteColors =
    [
        "#3B82F6", // Blue
        "#8B5CF6", // Purple
        "#EC4899", // Pink
        "#10B981", // Emerald
        "#F59E0B", // Amber
        "#06B6D4", // Cyan
        "#6366F1", // Indigo
        "#14B8A6", // Teal
        "#F43F5E", // Rose
        "#F97316"  // Orange
    ];

    private static readonly ConcurrentDictionary<string, IBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetInitials(string? displayName, string? jid)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return GetInitials(displayName);
        }

        return GetInitials(jid);
    }

    public static string GetInitials(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "?";

        var clean = text.Trim();
        var atIndex = clean.IndexOf('@');
        if (atIndex > 0)
        {
            clean = clean[..atIndex];
        }

        var parts = clean.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";

        if (parts.Length == 1)
        {
            var word = parts[0];
            return word.Length >= 2
                ? char.ToUpperInvariant(word[0]).ToString() + char.ToLowerInvariant(word[1])
                : char.ToUpperInvariant(word[0]).ToString();
        }

        return (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[1][0])).ToString();
    }

    public static IBrush GetAvatarColorBrush(string? text)
    {
        var key = string.IsNullOrWhiteSpace(text) ? "default" : text.Trim().ToLowerInvariant();
        return BrushCache.GetOrAdd(key, k =>
        {
            var hash = (uint)k.GetHashCode();
            var colorHex = PaletteColors[hash % (uint)PaletteColors.Length];
            return new SolidColorBrush(Color.Parse(colorHex));
        });
    }

    public static Bitmap? CreateBitmapFromBytes(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;

        try
        {
            using var ms = new MemoryStream(data);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    public static string ComputeSha1(byte[]? data)
    {
        if (data is null || data.Length == 0) return string.Empty;
        var hash = SHA1.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
