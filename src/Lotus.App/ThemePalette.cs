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
    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private void Set(string key, Color color) => ((SolidColorBrush)resources[key]).Color = color;
}
