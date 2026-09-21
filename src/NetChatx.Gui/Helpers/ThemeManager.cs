using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace NetChatx.Gui.Helpers;

public static class ThemeManager
{
    public const string ThemeDark = "Dark";
    public const string ThemeLight = "Light";
    public const string ThemeSystem = "System";

    public const string AccentCyan = "#00F0FF";
    public const string AccentPurple = "#A855F7";
    public const string AccentEmerald = "#10B981";
    public const string AccentAmber = "#F59E0B";

    public static string CurrentThemeMode { get; private set; } = ThemeDark;
    public static string CurrentAccentColor { get; private set; } = AccentCyan;

    public static void ApplyTheme(string themeMode, string accentColor)
    {
        CurrentThemeMode = !string.IsNullOrWhiteSpace(themeMode) ? themeMode : ThemeDark;
        CurrentAccentColor = !string.IsNullOrWhiteSpace(accentColor) ? accentColor : AccentCyan;

        if (Application.Current is null) return;

        // 1. Apply Theme Variant
        Application.Current.RequestedThemeVariant = CurrentThemeMode switch
        {
            ThemeLight => ThemeVariant.Light,
            ThemeSystem => ThemeVariant.Default,
            _ => ThemeVariant.Dark
        };

        // 2. Apply Accent Palette
        ApplyAccentPalette(CurrentAccentColor);
    }

    private static void ApplyAccentPalette(string hexColor)
    {
        if (Application.Current is null) return;

        var norm = (hexColor ?? string.Empty).Trim().ToUpperInvariant();
        if (norm.StartsWith("#FF") && norm.Length == 9)
        {
            norm = "#" + norm[3..];
        }
        else if (norm.StartsWith("FF") && norm.Length == 8)
        {
            norm = "#" + norm[2..];
        }
        else if (!norm.StartsWith("#") && norm.Length == 6)
        {
            norm = "#" + norm;
        }

        Color mainColor;
        Color gradStart;
        Color gradEnd;
        Color gradHoverStart;
        Color gradHoverEnd;

        if (norm.Contains("A855F7") || norm == AccentPurple || norm == "PURPLE")
        {
            mainColor = Color.Parse("#A855F7");
            gradStart = Color.Parse("#6D28D9");
            gradEnd = Color.Parse("#C084FC");
            gradHoverStart = Color.Parse("#7C3AED");
            gradHoverEnd = Color.Parse("#D8B4FE");
        }
        else if (norm.Contains("10B981") || norm == AccentEmerald || norm == "EMERALD" || norm == "GREEN")
        {
            mainColor = Color.Parse("#10B981");
            gradStart = Color.Parse("#047857");
            gradEnd = Color.Parse("#34D399");
            gradHoverStart = Color.Parse("#059669");
            gradHoverEnd = Color.Parse("#6EE7B7");
        }
        else if (norm.Contains("F59E0B") || norm == AccentAmber || norm == "AMBER" || norm == "ORANGE")
        {
            mainColor = Color.Parse("#F59E0B");
            gradStart = Color.Parse("#B45309");
            gradEnd = Color.Parse("#FBBF24");
            gradHoverStart = Color.Parse("#D97706");
            gradHoverEnd = Color.Parse("#FCD34D");
        }
        else
        {
            mainColor = Color.Parse("#00F0FF");
            gradStart = Color.Parse("#7C3AED");
            gradEnd = Color.Parse("#00C8FF");
            gradHoverStart = Color.Parse("#8B5CF6");
            gradHoverEnd = Color.Parse("#38BDF8");
        }

        var gradBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(gradStart, 0.0),
                new GradientStop(gradEnd, 1.0)
            }
        };

        var gradHoverBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(gradHoverStart, 0.0),
                new GradientStop(gradHoverEnd, 1.0)
            }
        };

        Application.Current.Resources["NetChatxCyan"] = mainColor;
        Application.Current.Resources["NetChatxCyanBrush"] = new SolidColorBrush(mainColor);
        Application.Current.Resources["NetChatxCyberGradient"] = gradBrush;
        Application.Current.Resources["NetChatxCyberGradientHover"] = gradHoverBrush;
    }
}
