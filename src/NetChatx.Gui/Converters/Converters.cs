using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using NetChatx.Storage.Models;

namespace NetChatx.Gui.Converters;

public sealed class DirectionToAlignmentConverter : IValueConverter
{
    public static readonly DirectionToAlignmentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageDirection direction)
        {
            return direction == MessageDirection.Outbound ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DirectionToBackgroundConverter : IValueConverter
{
    public static readonly DirectionToBackgroundConverter Instance = new();

    public static IBrush OutboundBrush { get; set; } = new SolidColorBrush(Color.Parse("#2563EB")); // Primary Blue
    public static IBrush InboundBrush { get; set; } = new SolidColorBrush(Color.Parse("#1E293B"));  // Obsidian Slate Glass

    public static void SetColors(string outboundHex, string inboundHex)
    {
        if (Color.TryParse(outboundHex, out var outColor))
        {
            OutboundBrush = new SolidColorBrush(outColor);
        }
        if (Color.TryParse(inboundHex, out var inColor))
        {
            InboundBrush = new SolidColorBrush(inColor);
        }
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageDirection direction)
        {
            return direction == MessageDirection.Outbound ? OutboundBrush : InboundBrush;
        }
        return InboundBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DirectionToForegroundConverter : IValueConverter
{
    public static readonly DirectionToForegroundConverter Instance = new();

    private static readonly IBrush WhiteBrush = Brushes.White;
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#F1F5F9"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageDirection direction)
        {
            return direction == MessageDirection.Outbound ? WhiteBrush : DefaultBrush;
        }
        return DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PresenceToBrushConverter : IValueConverter
{
    public static readonly PresenceToBrushConverter Instance = new();

    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#10B981")); // Neon Emerald
    private static readonly IBrush AmberBrush = new SolidColorBrush(Color.Parse("#F59E0B")); // Vibrant Amber
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#F43F5E"));   // Neon Rose
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.Parse("#64748B"));  // Muted Slate

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var show = value?.ToString()?.ToLowerInvariant();
        return show switch
        {
            "available" or "online" or "chat" => GreenBrush,
            "away" or "xa" => AmberBrush,
            "dnd" => RedBrush,
            _ => GrayBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToLockIconConverter : IValueConverter
{
    public static readonly BoolToLockIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "🔒 OMEMO" : "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
