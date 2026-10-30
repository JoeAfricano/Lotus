using System.Globalization;

namespace Lotus.Core.Themes;

// A plain sRGB color with the math the theme engine needs: hex, HSL, mixing and WCAG contrast.
public readonly record struct ThemeColor(byte R, byte G, byte B)
{
    public static ThemeColor White => new(255, 255, 255);
    public static ThemeColor Black => new(0, 0, 0);

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public override string ToString() => Hex;

    public static bool TryParse(string? hex, out ThemeColor color)
    {
        color = default;
        if (hex is null) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => new string(c, 2)));
        if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
    public static ThemeColor Parse(string hex) => TryParse(hex, out var c) ? c : throw new FormatException("Not a color: " + hex);

    // WCAG relative luminance and contrast ratio (1–21).
    public double Luminance
    {
        get
        {
            static double L(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * L(R) + 0.7152 * L(G) + 0.0722 * L(B);
        }
    }
    public double Contrast(ThemeColor other)
    {
        var (a, b) = (Luminance, other.Luminance);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    public bool IsLight => Luminance > 0.4;

    // Linear mix: 0 = this, 1 = other.
    public ThemeColor Mix(ThemeColor other, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return new(M(R, other.R), M(G, other.G), M(B, other.B));
    }
    public ThemeColor Lighten(double t) => Mix(White, t);
    public ThemeColor Darken(double t) => Mix(Black, t);

    // HSL (hue 0–360, saturation and lightness 0–1).
    public (double H, double S, double L) Hsl
    {
        get
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
            if (d < 1e-9) return (0, 0, l);
            var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h * 60, s, l);
        }
    }
    public static ThemeColor FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360; s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
        var c = (1 - Math.Abs(2 * l - 1)) * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = l - c / 2;
        var (r, g, b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        byte B(double v) => (byte)Math.Round(Math.Clamp((v + m) * 255, 0, 255));
        return new(B(r), B(g), B(b));
    }

    // Moves this color toward white (or black) in small steps until it reaches the contrast asked against a background.
    // Returns the first color that passes; the hue is kept as long as possible.
    public ThemeColor Readable(ThemeColor background, double minimum)
    {
        if (Contrast(background) >= minimum) return this;
        var toward = background.IsLight ? Black : White;
        for (var t = 0.05; t <= 1.0001; t += 0.05)
        {
            var c = Mix(toward, t);
            if (c.Contrast(background) >= minimum) return c;
        }
        return toward;
    }
}
