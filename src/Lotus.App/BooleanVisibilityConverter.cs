using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
namespace Lotus.App;
public sealed class BooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (parameter as string == "Invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Dims secondary items (for example disabled startup entries) without hiding them.
public sealed class BooleanOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? 0.55 : 1.0;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Picks one of two resources (brushes, thickness) from a boolean, e.g. to highlight the selected profile card.
public sealed class BooleanChoiceConverter : IValueConverter
{
    public object? TrueValue { get; set; }
    public object? FalseValue { get; set; }
    public object? Convert(object value, Type targetType, object parameter, string language) => value is true ? TrueValue : FalseValue;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Visible when the value is a non-empty string (or any non-null object); "Invert" flips it.
public sealed class PresenceVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var present = value is string s ? !string.IsNullOrWhiteSpace(s) : value is not null;
        if (parameter as string == "Invert") present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Loads local cover art decoded at card size, so large launcher images do not stay in memory at full resolution.
public sealed class CoverImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path) return null;
        // Store art (https) as well as local cover files.
        if (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out var web)) return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(web) { DecodePixelWidth = 360 };
        if (!File.Exists(path)) return null;
        try { return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path)) { DecodePixelWidth = 360 }; }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException) { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// "#RRGGBB" to a brush (accent swatches).
public sealed class HexBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string s || s.Length != 7 || s[0] != '#') return null;
        var c = System.Convert.ToUInt32(s[1..], 16);
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

// Diary: score colour by band ("good" / "mixed" / "bad" / "none") and a colour per game status.
public sealed class DiaryBrushConverter : IValueConverter
{
    private static Microsoft.UI.Xaml.Media.SolidColorBrush B(byte r, byte g, byte b, byte a = 255) => new(Windows.UI.Color.FromArgb(a, r, g, b));
    public object Convert(object value, Type targetType, object parameter, string language) => (value as string) switch
    {
        "good" => B(102, 214, 140), "mixed" => B(255, 204, 80), "bad" => B(255, 110, 110),
        "Completed" => B(64, 200, 170), "Playing" => B(242, 123, 168), "Want" => B(156, 120, 240), "Paused" => B(240, 194, 125), "Dropped" => B(160, 140, 150),
        // Achievement tiers and Steam rarity bands
        "bronze" => B(214, 140, 82), "silver" => B(196, 204, 214), "gold" => B(244, 200, 110), "platinum" => B(150, 225, 240),
        "legendary" => B(255, 170, 60), "epic" => B(190, 120, 255), "rare" => B(90, 170, 255), "uncommon" => B(102, 214, 140), "common" => B(179, 161, 172),
        _ => B(179, 161, 172),
    };
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Medal fill for an achievement tier (a soft metallic gradient).
public sealed class TierFillConverter : IValueConverter
{
    private static Windows.UI.Color C(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(255, r, g, b);
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var (light, dark) = (value as string) switch
        {
            "bronze" => (C(240, 178, 122), C(160, 92, 44)),
            "silver" => (C(236, 240, 245), C(140, 150, 166)),
            "gold" => (C(255, 228, 150), C(214, 150, 50)),
            "platinum" => (C(210, 248, 255), C(110, 170, 200)),
            _ => (C(90, 70, 82), C(52, 38, 48)),
        };
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        brush.GradientStops.Add(new() { Color = light, Offset = 0 });
        brush.GradientStops.Add(new() { Color = dark, Offset = 1 });
        return brush;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Locked badges are faded; unlocked are full.
public sealed class UnlockedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? 1.0 : 0.38;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Design package fallback art for games without a cover: one of three abstract Lotus covers, picked by the name
// (stable per game). Never presented as the game's own art.
public sealed class FallbackCoverConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var name = value as string ?? "";
        var n = 1 + (int)((uint)name.Aggregate(17, (h, c) => h * 31 + c) % 3);
        return new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Concept", $"capa-{n}.svg")));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
