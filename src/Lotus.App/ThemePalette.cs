using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Lotus.App;

// Update shared brushes in place so existing controls change without restarting or flashing the window.
// Lotus identity: plum-black surfaces, crimson/magenta accent, rose-gold highlights. "Graphite" keeps the same
// accents on neutral surfaces. Surfaces stay translucent for Mica; high contrast uses opaque system colors.
public sealed class ThemePalette(ResourceDictionary resources)
{
    public void Apply(string theme)
    {
        var hc = new AccessibilitySettings().HighContrast;
        var system = new UISettings();
        var graphite = theme == "Graphite";
        var clear = Argb(0, 0, 0, 0);
        var background = hc ? system.GetColorValue(UIColorType.Background) : graphite ? Argb(0xEB, 28, 28, 34) : Argb(0xEB, 18, 9, 15);
        var foreground = hc ? system.GetColorValue(UIColorType.Foreground) : Argb(255, 248, 240, 243);
        Set("WindowBrush", hc ? background : graphite ? Argb(255, 24, 24, 29) : Argb(255, 23, 11, 19));
        Set("ShellBrush", background); Set("NavigationViewContentBackground", background);
        Set("PaneBrush", hc ? background : clear);
        Set("NavigationViewDefaultPaneBackground", hc ? background : clear);
        Set("NavigationViewExpandedPaneBackground", hc ? background : clear);
        Set("CardBrush", hc ? background : graphite ? Argb(0xB8, 42, 42, 50) : Argb(0xB8, 31, 15, 25));
        Set("CardHoverBrush", hc ? background : graphite ? Argb(0xCC, 52, 52, 62) : Argb(0xCC, 42, 21, 33));
        Set("ElevatedBrush", hc ? background : graphite ? Argb(0xD9, 48, 46, 56) : Argb(0xD9, 38, 19, 32));
        Set("CardBorderBrush", hc ? foreground : Argb(0x1A, 255, 228, 238));
        Set("NavigationViewContentGridBorderBrush", hc ? foreground : Argb(0x1A, 255, 228, 238));
        Set("DividerBrush", hc ? foreground : Argb(0x12, 255, 228, 238));
        Set("MainTextBrush", foreground);
        Set("MutedBrush", hc ? foreground : Argb(255, 179, 161, 172));
        Set("OnRoseBrush", hc ? background : Argb(255, 255, 246, 249));
        Set("RoseBrush", hc ? foreground : Argb(255, 242, 123, 168));
        Set("GoldBrush", hc ? foreground : Argb(255, 238, 193, 168));
        Set("AccentSoftBrush", hc ? background : Argb(0x2E, 214, 48, 111));
        Set("BadgeBrush", hc ? background : Argb(0x2E, 214, 48, 111));
        Set("SuccessBrush", hc ? foreground : Argb(255, 127, 209, 168));
        Set("WarningBrush", hc ? foreground : Argb(255, 240, 194, 125));
        Set("ErrorBrush", hc ? foreground : Argb(255, 255, 143, 163));
    }

    // Accent color chosen by the player: buttons, selection, progress, badges and the rose text all follow it.
    public void ApplyAccent(string accent)
    {
        if (new AccessibilitySettings().HighContrast) return;
        var (main, light, dark, from, to) = accent switch
        {
            "crimson" => (Argb(255, 198, 40, 57), Argb(255, 240, 106, 122), Argb(255, 150, 24, 38), Argb(255, 224, 56, 76), Argb(255, 142, 18, 34)),
            "gold" => (Argb(255, 196, 132, 52), Argb(255, 238, 192, 122), Argb(255, 150, 96, 30), Argb(255, 227, 163, 80), Argb(255, 154, 100, 32)),
            "violet" => (Argb(255, 138, 79, 214), Argb(255, 183, 147, 242), Argb(255, 100, 52, 168), Argb(255, 156, 92, 240), Argb(255, 94, 42, 168)),
            _ => (Argb(255, 214, 48, 111), Argb(255, 242, 123, 168), Argb(255, 184, 37, 93), Argb(255, 232, 55, 138), Argb(255, 168, 18, 79)),
        };
        var darkMode = accent == "dark";
        if (darkMode) // dark buttons and badges; rose stays only on small details (selection, progress, highlights)
        {
            (from, to) = (Argb(255, 62, 50, 57), Argb(255, 36, 28, 33));
        }
        Set("RoseBrush", light);
        Set("CardHoverBorderBrush", darkMode ? Argb(0x59, 242, 123, 168) : Color.FromArgb(0x66, light.R, light.G, light.B));
        // Menu: selected item filled with the accent, band tinted with it (gold selection line stays).
        var tint = darkMode ? Argb(255, 120, 96, 108) : main;
        Set("NavigationViewItemBackgroundSelected", Color.FromArgb(0x73, tint.R, tint.G, tint.B)); Set("NavigationViewItemBackgroundSelectedPointerOver", Color.FromArgb(0x8C, tint.R, tint.G, tint.B));
        Set("NavigationViewItemBackgroundSelectedPressed", Color.FromArgb(0x66, tint.R, tint.G, tint.B));
        if (resources["NavigationViewTopPaneBackground"] is LinearGradientBrush band && band.GradientStops.Count >= 3)
        { band.GradientStops[0].Color = Color.FromArgb(0x4D, dark.R, dark.G, dark.B); band.GradientStops[1].Color = Color.FromArgb(0x2E, (byte)(dark.R / 2), (byte)(dark.G / 2), (byte)(dark.B / 2)); }
        var soft = darkMode ? Argb(0x22, 255, 255, 255) : Color.FromArgb(0x2E, main.R, main.G, main.B);
        Set("AccentSoftBrush", soft); Set("BadgeBrush", soft);
        Set("AccentFillColorDefaultBrush", main); Set("AccentFillColorSecondaryBrush", light); Set("AccentFillColorTertiaryBrush", dark);
        resources["SystemAccentColor"] = main; resources["SystemAccentColorLight1"] = light; resources["SystemAccentColorLight2"] = light; resources["SystemAccentColorDark1"] = dark;
        if (resources["AccentGradientBrush"] is LinearGradientBrush g && g.GradientStops.Count >= 2) { g.GradientStops[0].Color = from; g.GradientStops[1].Color = to; }
    }
    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private void Set(string key, Color color) => ((SolidColorBrush)resources[key]).Color = color;
}
