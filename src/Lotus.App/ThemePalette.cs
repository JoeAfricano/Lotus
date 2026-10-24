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
            : theme == "Graphite" ? Color.FromArgb(255, 35, 35, 45) : Color.FromArgb(255, 12, 11, 18);
        var pane = highContrast ? background : theme == "Graphite" ? Color.FromArgb(255, 43, 43, 55) : Color.FromArgb(255, 17, 16, 24);
        var card = highContrast ? background : theme == "Graphite" ? Color.FromArgb(255, 49, 49, 63) : Color.FromArgb(255, 24, 20, 30);
        var foreground = highContrast ? system.GetColorValue(UIColorType.Foreground) : Color.FromArgb(255, 247, 241, 246);
        Set("ShellBrush", background); Set("NavigationViewContentBackground", background);
        Set("PaneBrush", pane); Set("NavigationViewDefaultPaneBackground", pane); Set("NavigationViewExpandedPaneBackground", pane);
        Set("CardBrush", card); Set("MainTextBrush", foreground);
        Set("MutedBrush", highContrast ? foreground : Color.FromArgb(255, 189, 177, 188));
        Set("OnRoseBrush", highContrast ? background : Color.FromArgb(255, 23, 16, 23));
        Set("RoseBrush", highContrast ? foreground : Color.FromArgb(255, 229, 132, 177));
        Set("CardBorderBrush", highContrast ? foreground : Color.FromArgb(255, 73, 48, 68));
        Set("ElevatedBrush", highContrast ? background : theme == "Graphite" ? Color.FromArgb(255, 55, 45, 59) : Color.FromArgb(255, 33, 23, 35));
        Set("SuccessBrush", highContrast ? foreground : Color.FromArgb(255, 135, 198, 165));
        Set("WarningBrush", highContrast ? foreground : Color.FromArgb(255, 231, 190, 122));
        Set("ErrorBrush", highContrast ? foreground : Color.FromArgb(255, 243, 162, 170));
        Set("BadgeBrush", highContrast ? background : Color.FromArgb(255, 43, 20, 36));
    }
    private void Set(string key, Color color) => ((SolidColorBrush)resources[key]).Color = color;
}
