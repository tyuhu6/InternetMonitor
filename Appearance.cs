using System;
using System.Windows;
using System.Windows.Media;

namespace InternetMonitor;

/// <summary>字体、字号与配色的统一主题，改动后立即生效。</summary>
public sealed class Appearance
{
    public string FontFamilyName { get; set; } = "Microsoft YaHei UI";
    public double FontScale { get; set; } = 1.0;
    public Color Background { get; set; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
    public Color Text { get; set; } = Color.FromRgb(0x10, 0x13, 0x18);
    public Color Down { get; set; } = Color.FromRgb(0x2F, 0x6B, 0xFF);
    public Color Up { get; set; } = Color.FromRgb(0xE8, 0x89, 0x0C);

    public bool IsDark => Luminance(Background) < 0.5;
    public Color Border => Shift(Background, IsDark ? 0.11 : -0.075);
    public Color ChartBackground => Shift(Background, IsDark ? -0.13 : -0.04);
    public Color Surface => IsDark ? Shift(Background, 0.075) : Shift(Background, -0.03);
    public Color Secondary => Blend(Text, Background, 0.42);
    public Color Muted => Blend(Text, Background, 0.62);
    public Color SwitchTrack => Shift(Background, IsDark ? 0.15 : -0.08);
    public Color GridLine => IsDark
        ? Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)
        : Color.FromArgb(0x16, 0x00, 0x00, 0x00);

    public static Appearance FromState(AppState state)
    {
        return new Appearance
        {
            FontFamilyName = string.IsNullOrWhiteSpace(state.FontFamily) ? "Microsoft YaHei UI" : state.FontFamily,
            FontScale = Math.Clamp(state.FontScale, 0.8, 1.5),
            Background = ParseHex(state.BgColor, Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Text = ParseHex(state.TextColor, Color.FromRgb(0x10, 0x13, 0x18)),
            Down = ParseHex(state.DownColor, Color.FromRgb(0x2F, 0x6B, 0xFF)),
            Up = ParseHex(state.UpColor, Color.FromRgb(0xE8, 0x89, 0x0C))
        };
    }

    /// <summary>写入应用级资源，所有 DynamicResource 引用会立即刷新。</summary>
    public void Apply()
    {
        var res = System.Windows.Application.Current.Resources;

        res["AppFontFamily"] = new FontFamily(FontFamilyName);
        res["BgBrush"] = CardBrush();
        res["SurfaceBrush"] = Solid(Surface);
        res["TextPrimary"] = Solid(Text);
        res["TextSecondary"] = Solid(Secondary);
        res["TextMuted"] = Solid(Muted);
        res["BorderBrushSoft"] = Solid(Border);
        res["SwitchTrack"] = Solid(SwitchTrack);
        res["AccentDown"] = Solid(Down);
        res["AccentUp"] = Solid(Up);
        res["HoverBrush"] = Solid(IsDark
            ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x12, 0x00, 0x00, 0x00));

        res["FontSizeTitle"] = 12 * FontScale;
        res["FontSizeValue"] = 28 * FontScale;
        res["FontSizeUnit"] = 11 * FontScale;
        res["FontSizeLabel"] = 11 * FontScale;
        res["FontSizeSmall"] = 10.5 * FontScale;
        res["FontSizeFooter"] = 10.5 * FontScale;
    }

    public static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>窗口底色：深色下为极轻微渐变，浅色下保持纯白。</summary>
    private Brush CardBrush()
    {
        if (!IsDark)
            return Solid(Background);

        var brush = new LinearGradientBrush(
            Shift(Background, 0.045),
            Background,
            new Point(0, 0),
            new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    public static Color ParseHex(string? hex, Color fallback)
    {
        return TryParseHex(hex, out var color) ? color : fallback;
    }

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            if (ColorConverter.ConvertFromString(hex.Trim()) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // 非法颜色值
        }

        return false;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color Blend(Color from, Color to, double ratio)
    {
        double t = Math.Clamp(ratio, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    /// <summary>正数向白色靠近，负数向黑色靠近。</summary>
    public static Color Shift(Color color, double amount)
    {
        if (amount >= 0)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, color.R + (255 - color.R) * amount),
                (byte)Math.Min(255, color.G + (255 - color.G) * amount),
                (byte)Math.Min(255, color.B + (255 - color.B) * amount));
        }

        double k = 1 + amount;
        return Color.FromRgb((byte)(color.R * k), (byte)(color.G * k), (byte)(color.B * k));
    }

    private static double Luminance(Color color)
        => (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
}
