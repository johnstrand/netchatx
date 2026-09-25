using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Stanza.Gui.Helpers;

public static class ThemeManager
{
    public const string ThemeDark = "Dark";
    public const string ThemeLight = "Light";
    public const string ThemeSystem = "System";

    public const string AccentCyan = "#00F0FF";
    public const string AccentPurple = "#A855F7";
    public const string AccentEmerald = "#10B981";
    public const string AccentAmber = "#F59E0B";
    public const string AccentCrimson = "#EF4444";
    public const string AccentRose = "#EC4899";
    public const string AccentIndigo = "#6366F1";
    public const string AccentOrange = "#F97316";

    public static readonly IReadOnlyList<string> CuratedAccents =
    [
        AccentCyan,
        AccentPurple,
        AccentEmerald,
        AccentAmber,
        AccentCrimson,
        AccentRose,
        AccentIndigo,
        AccentOrange
    ];

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

    public static string NormalizeHex(string? hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
        {
            return AccentCyan;
        }

        var norm = hexColor.Trim().ToUpperInvariant();
        if (norm.StartsWith("#FF") && norm.Length == 9)
        {
            norm = "#" + norm[3..];
        }
        else if (norm.StartsWith("FF") && norm.Length == 8)
        {
            norm = "#" + norm[2..];
        }
        else if (!norm.StartsWith('#') && (norm.Length == 6 || norm.Length == 8))
        {
            norm = "#" + norm;
        }
        else if (norm.Length == 3 || (norm.StartsWith('#') && norm.Length == 4))
        {
            var raw = norm.TrimStart('#');
            norm = $"#{raw[0]}{raw[0]}{raw[1]}{raw[1]}{raw[2]}{raw[2]}";
        }

        if (!Color.TryParse(norm, out _))
        {
            return AccentCyan;
        }

        return norm;
    }

    public static void ApplyAccentPalette(string hexColor)
    {
        if (Application.Current is null) return;

        var norm = NormalizeHex(hexColor);

        Color mainColor;
        Color gradStart;
        Color gradEnd;
        Color gradHoverStart;
        Color gradHoverEnd;

        if (norm.Contains("A855F7", StringComparison.OrdinalIgnoreCase) || norm == AccentPurple || norm == "PURPLE")
        {
            mainColor = Color.Parse("#A855F7");
            gradStart = Color.Parse("#6D28D9");
            gradEnd = Color.Parse("#C084FC");
            gradHoverStart = Color.Parse("#7C3AED");
            gradHoverEnd = Color.Parse("#D8B4FE");
        }
        else if (norm.Contains("10B981", StringComparison.OrdinalIgnoreCase) || norm == AccentEmerald || norm == "EMERALD" || norm == "GREEN")
        {
            mainColor = Color.Parse("#10B981");
            gradStart = Color.Parse("#047857");
            gradEnd = Color.Parse("#34D399");
            gradHoverStart = Color.Parse("#059669");
            gradHoverEnd = Color.Parse("#6EE7B7");
        }
        else if (norm.Contains("F59E0B", StringComparison.OrdinalIgnoreCase) || norm == AccentAmber || norm == "AMBER")
        {
            mainColor = Color.Parse("#F59E0B");
            gradStart = Color.Parse("#B45309");
            gradEnd = Color.Parse("#FBBF24");
            gradHoverStart = Color.Parse("#D97706");
            gradHoverEnd = Color.Parse("#FCD34D");
        }
        else if (norm.Contains("EF4444", StringComparison.OrdinalIgnoreCase) || norm == AccentCrimson || norm == "CRIMSON" || norm == "RED")
        {
            mainColor = Color.Parse("#EF4444");
            gradStart = Color.Parse("#B91C1C");
            gradEnd = Color.Parse("#F87171");
            gradHoverStart = Color.Parse("#DC2626");
            gradHoverEnd = Color.Parse("#FCA5A5");
        }
        else if (norm.Contains("EC4899", StringComparison.OrdinalIgnoreCase) || norm == AccentRose || norm == "ROSE" || norm == "PINK")
        {
            mainColor = Color.Parse("#EC4899");
            gradStart = Color.Parse("#BE185D");
            gradEnd = Color.Parse("#F472B6");
            gradHoverStart = Color.Parse("#DB2777");
            gradHoverEnd = Color.Parse("#F9A8D4");
        }
        else if (norm.Contains("6366F1", StringComparison.OrdinalIgnoreCase) || norm == AccentIndigo || norm == "INDIGO")
        {
            mainColor = Color.Parse("#6366F1");
            gradStart = Color.Parse("#4338CA");
            gradEnd = Color.Parse("#818CF8");
            gradHoverStart = Color.Parse("#4F46E5");
            gradHoverEnd = Color.Parse("#A5B4FC");
        }
        else if (norm.Contains("F97316", StringComparison.OrdinalIgnoreCase) || norm == AccentOrange || norm == "ORANGE")
        {
            mainColor = Color.Parse("#F97316");
            gradStart = Color.Parse("#C2410C");
            gradEnd = Color.Parse("#FB923C");
            gradHoverStart = Color.Parse("#EA580C");
            gradHoverEnd = Color.Parse("#FDBA74");
        }
        else if (norm.Contains("00F0FF", StringComparison.OrdinalIgnoreCase) || norm == AccentCyan || norm == "CYAN")
        {
            mainColor = Color.Parse("#00F0FF");
            gradStart = Color.Parse("#7C3AED");
            gradEnd = Color.Parse("#00C8FF");
            gradHoverStart = Color.Parse("#8B5CF6");
            gradHoverEnd = Color.Parse("#38BDF8");
        }
        else if (Color.TryParse(norm, out var customColor))
        {
            // Dynamic gradient derivation from custom hex color
            mainColor = customColor;
            RgbToHsl(customColor.R, customColor.G, customColor.B, out double h, out double s, out double l);

            gradStart = HslToRgb(h, s, Math.Clamp(l * 0.72, 0.12, 0.82));
            gradEnd = HslToRgb(h, s, Math.Clamp(l + 0.16, 0.22, 0.96));
            gradHoverStart = HslToRgb(h, s, Math.Clamp(l * 0.82, 0.16, 0.88));
            gradHoverEnd = HslToRgb(h, s, Math.Clamp(l + 0.22, 0.26, 0.99));
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

        Application.Current.Resources["StanzaCyan"] = mainColor;
        Application.Current.Resources["StanzaCyanBrush"] = new SolidColorBrush(mainColor);
        Application.Current.Resources["StanzaCyberGradient"] = gradBrush;
        Application.Current.Resources["StanzaCyberGradientHover"] = gradHoverBrush;
    }

    public static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta == 0)
        {
            h = 0;
            s = 0;
        }
        else
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == rd)
                h = ((gd - bd) / delta) + (gd < bd ? 6 : 0);
            else if (max == gd)
                h = ((bd - rd) / delta) + 2;
            else
                h = ((rd - gd) / delta) + 4;

            h /= 6.0;
        }
    }

    public static Color HslToRgb(double h, double s, double l, byte a = 255)
    {
        double r, g, b;

        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            static double HueToRgb(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
                if (t < 1.0 / 2.0) return q;
                if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
                return p;
            }

            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        return new Color(a, (byte)Math.Clamp((int)(r * 255.0), 0, 255), (byte)Math.Clamp((int)(g * 255.0), 0, 255), (byte)Math.Clamp((int)(b * 255.0), 0, 255));
    }
}
