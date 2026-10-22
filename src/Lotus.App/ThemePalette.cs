using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Lotus.App;

// Update shared brushes in place so existing controls change without restarting or flashing the window.
public sealed class ThemePalette(ResourceDictionary resources)
{
    public void Apply(string theme)
    {
        var highContrast = new AccessibilitySettings().HighContrast;
        var system = new UISettings();
        var background = highContrast ? system.GetColorValue(UIColorType.Background)
            : theme == "Graphite" ? Color.FromArgb(255, 35, 35, 45) : Color.FromArgb(255, 10, 10, 15);
        var pane = highContrast ? background : theme == "Graphite" ? Color.FromArgb(255, 43, 43, 55) : Color.FromArgb(255, 16, 16, 22);
        var card = highContrast ? background : theme == "Graphite" ? Color.FromArgb(255, 49, 49, 63) : Color.FromArgb(255, 18, 18, 25);
        var foreground = highContrast ? system.GetColorValue(UIColorType.Foreground) : Color.FromArgb(255, 245, 239, 243);
        Set("ShellBrush", background); Set("NavigationViewContentBackground", background);
        Set("PaneBrush", pane); Set("NavigationViewDefaultPaneBackground", pane); Set("NavigationViewExpandedPaneBackground", pane);
        Set("CardBrush", card); Set("MainTextBrush", foreground);
        Set("MutedBrush", highContrast ? foreground : Color.FromArgb(255, 200, 194, 207));
        Set("RoseBrush", highContrast ? foreground : Color.FromArgb(255, 255, 145, 181));
        Set("CardBorderBrush", highContrast ? foreground : Color.FromArgb(255, 104, 51, 74));
        Set("BadgeBrush", highContrast ? background : Color.FromArgb(255, 43, 20, 36));
    }
    private void Set(string key, Color color) => ((SolidColorBrush)resources[key]).Color = color;
}
