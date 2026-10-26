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
        _ => B(179, 161, 172),
    };
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Locked badges are faded; unlocked are full.
public sealed class UnlockedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? 1.0 : 0.38;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
