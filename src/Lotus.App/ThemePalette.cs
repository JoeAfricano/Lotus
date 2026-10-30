using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Lotus.App;

// The one authority over Lotus colors ("Rosé Quartz" tokens): shared brushes are updated in place, so every control
// changes live without restarting. Modes: Midnight (dark), Light, System (follows Windows) and Graphite (dark on
// neutral surfaces). The accent is a pair: a fill for buttons/selection (white text on it) and a text color tuned per
// mode. Success/warning/error never follow the accent. High contrast wins over everything and uses system colors.
public sealed class ThemePalette
{
    private readonly ResourceDictionary resources;
    private readonly UISettings windows = new();
    private string theme = "Midnight", accent = "rose";
    private Lotus.Core.Themes.ThemeSpec? custom;
    private readonly Dictionary<string, Color[]> originalStops = [];
    public Lotus.Core.Themes.ThemeTokens? CustomTokens { get; private set; }

    public ThemePalette(ResourceDictionary resources)
    {
        Instance = this;
        this.resources = resources;
        foreach (var key in new[] { "GoldGradientBrush", "PetalBrush" })
            if (resources.TryGetValue(key, out var v) && v is LinearGradientBrush g) originalStops[key] = [.. g.GradientStops.Select(s => s.Color)];
        // "System" follows Windows live (light/dark switch, high contrast on/off).
        windows.ColorValuesChanged += (_, _) => Dispatcher?.TryEnqueue(Refresh); // also fires when high contrast turns on/off
    }

    public static ThemePalette? Instance { get; private set; } // for code-built art (banners) that should follow custom themes
    public Microsoft.UI.Dispatching.DispatcherQueue? Dispatcher { get; set; }
    public static ElementTheme Element { get; private set; } = ElementTheme.Dark; // for dialogs and other popups
    public bool IsLight => Element == ElementTheme.Light;
    public event EventHandler? Changed;
    private static bool HighContrast => new AccessibilitySettings().HighContrast;

    public void Apply(string themeName, Lotus.Core.Themes.ThemeSpec? customTheme = null) { theme = themeName; custom = customTheme; Refresh(); }
    // Theme Studio: every change previews live on the whole app (not saved until the player saves).
    public void Preview(Lotus.Core.Themes.ThemeSpec spec) { theme = "Custom"; custom = spec; Refresh(); }
    public bool IsCustom => theme == "Custom" && custom is not null;
    public Lotus.Core.Themes.ThemeSpec? Custom => IsCustom ? custom : null;
    public void ApplyAccent(string accentName) { accent = accentName; Refresh(); }

    private bool SystemIsLight()
    {
        var bg = windows.GetColorValue(UIColorType.Background);
        return bg.R + bg.G + bg.B > 382;
    }

    private void Refresh()
    {
        var hc = HighContrast;
        CustomTokens = !hc && IsCustom ? Lotus.Core.Themes.ThemeEngine.Derive(custom!) : null;
        var light = !hc && (CustomTokens?.Light ?? (theme == "Light" || (theme == "System" && SystemIsLight())));
        var graphite = !hc && !light && theme == "Graphite";
        Element = light ? ElementTheme.Light : ElementTheme.Dark;
        if (hc) ApplyHighContrast();
        else if (CustomTokens is { } ct) ApplyCustom(ct);
        else { ApplyTokens(light, graphite); RestoreStops(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Surface and text tokens per mode (design: TEMAS-E-ACESSIBILIDADE.md).
    private void ApplyTokens(bool light, bool graphite)
    {
        var t = light ? Tokens.Light : graphite ? Tokens.Graphite : Tokens.Dark;
        var (fill, textDark, textLight) = Accent(accent);
        ApplySet(t, light, fill, light ? textLight : textDark, light ? Rgb(0x8E, 0x58, 0x1E) : Rgb(0xEE, 0xC1, 0xA8), Rgb(255, 255, 255), // rose-gold in the dark, a deep readable gold on porcelain
            light ? Rgb(0x24, 0x6C, 0x4D) : Rgb(0x8A, 0xD6, 0xB3), light ? Rgb(0x77, 0x50, 0x1B) : Rgb(0xEA, 0xC1, 0x83), light ? Rgb(0xB3, 0x22, 0x42) : Rgb(0xFF, 0x9C, 0xA6), 255);
    }

    // Every shared brush from one set of colors (built-in modes and custom themes alike).
    private void ApplySet(Tokens t, bool light, Color fill, Color accentText, Color gold, Color onAccent, Color success, Color warning, Color error, byte cardAlpha)
    {
        Set("WindowBrush", t.Canvas); Set("ShellBrush", t.Canvas); Set("NavigationViewContentBackground", t.Canvas);
        Set("PaneBrush", t.Shell); Set("NavigationViewDefaultPaneBackground", t.Shell); Set("NavigationViewExpandedPaneBackground", t.Shell);
        Set("CardBrush", WithAlpha(t.Card, cardAlpha)); Set("CardHoverBrush", WithAlpha(t.Raised, cardAlpha)); Set("ElevatedBrush", t.Raised); // sheets and popups stay solid, only cards follow the transparency
        Set("CardBorderBrush", t.Border); Set("NavigationViewContentGridBorderBrush", t.Border); Set("DividerBrush", t.Divider);
        Set("CardHoverBorderBrush", WithAlpha(accentText, 0x80));
        Set("MainTextBrush", t.Text); Set("MutedBrush", t.TextSecondary);
        Set("RoseBrush", accentText);
        Set("GoldBrush", gold);
        // The Lotus logo never follows the theme: fixed brand colors, only a deeper shade on light backgrounds so it stays readable.
        Set("BrandGoldBrush", light ? Rgb(0x8E, 0x58, 0x1E) : Rgb(0xEE, 0xC1, 0xA8));
        Set("BrandRoseBrush", light ? Rgb(0xB0, 0x19, 0x5A) : Rgb(0xF5, 0x8B, 0xB7));
        Set("BrandSoftBrush", light ? Argb(0x1F, 0xAD, 0x24, 0x5E) : Argb(0x33, 0xAD, 0x24, 0x5E));
        Set("OnRoseBrush", onAccent); Set("TextOnAccentFillColorPrimaryBrush", onAccent);
        Set("SubtleBrush", light ? Argb(0x1F, 0xB0, 0x19, 0x5A) : Argb(0x14, 255, 255, 255));
        Set("SuccessBrush", success); Set("WarningBrush", warning); Set("ErrorBrush", error);
        var soft = WithAlpha(fill, light ? (byte)0x1F : (byte)0x33);
        Set("AccentSoftBrush", soft); Set("BadgeBrush", soft);
        // Navigation: text, hover and the selected item.
        Set("NavigationViewItemForeground", light ? t.Text : t.TextSecondary); Set("NavigationViewItemForegroundPointerOver", t.Text); Set("NavigationViewItemForegroundSelected", t.Text);
        Set("TopNavigationViewItemForeground", light ? t.Text : t.TextSecondary); Set("TopNavigationViewItemForegroundPointerOver", t.Text); Set("TopNavigationViewItemForegroundPressed", t.Text);
        Set("TopNavigationViewItemForegroundSelected", t.Text); Set("TopNavigationViewItemForegroundSelectedPointerOver", t.Text); Set("TopNavigationViewItemForegroundSelectedPressed", t.Text);
        Set("NavigationViewItemBackgroundPointerOver", light ? WithAlpha(fill, 0x14) : Argb(0x12, 255, 255, 255));
        Set("NavigationViewItemBackgroundPressed", light ? WithAlpha(fill, 0x22) : Argb(0x0C, 255, 255, 255));
        var selected = light ? WithAlpha(fill, 0x2A) : t.Raised; // light: a clear rose pill on the current page
        Set("NavigationViewItemBackgroundSelected", selected); Set("NavigationViewItemBackgroundSelectedPointerOver", selected); Set("NavigationViewItemBackgroundSelectedPressed", selected);
        Set("NavigationViewSelectionIndicatorForeground", accentText);
        Set("SystemControlFocusVisualPrimaryBrush", light ? Rgb(0x28, 0x1C, 0x30) : Rgb(0xF8, 0xF5, 0xFA));
        if (resources["NavigationViewTopPaneBackground"] is LinearGradientBrush band && band.GradientStops.Count >= 3)
            foreach (var stop in band.GradientStops) stop.Color = t.Shell;
        // Accent fill (buttons, toggles, progress) — white text on all four design accents.
        var dark = Darken(fill, 0.82); var hover = Lighten(fill, 0.12);
        Set("AccentFillColorDefaultBrush", fill); Set("AccentFillColorSecondaryBrush", hover); Set("AccentFillColorTertiaryBrush", dark);
        resources["SystemAccentColor"] = fill; resources["SystemAccentColorLight1"] = hover; resources["SystemAccentColorLight2"] = accentText; resources["SystemAccentColorDark1"] = dark;
        if (resources["AccentGradientBrush"] is LinearGradientBrush g && g.GradientStops.Count >= 2) { g.GradientStops[0].Color = hover; g.GradientStops[1].Color = fill; }
        if (resources["HeroBrush"] is LinearGradientBrush hero && hero.GradientStops.Count >= 3)
        {
            hero.GradientStops[0].Color = WithAlpha(fill, light ? (byte)0x26 : (byte)0x40);
            hero.GradientStops[1].Color = WithAlpha(fill, light ? (byte)0x10 : (byte)0x1A);
            hero.GradientStops[2].Color = WithAlpha(t.Card, 0x10);
        }
    }

    // A custom theme: the derived (already readable) colors, the card transparency and the gold/petal gradients.
    private void ApplyCustom(Lotus.Core.Themes.ThemeTokens k)
    {
        static Color C(Lotus.Core.Themes.ThemeColor c) => Rgb(c.R, c.G, c.B);
        var t = new Tokens(C(k.Canvas), C(k.Shell), C(k.Card), C(k.Raised), C(k.Border), C(k.Divider), C(k.Text), C(k.TextSecondary));
        ApplySet(t, k.Light, C(k.AccentFill), C(k.AccentText), C(k.Detail), C(k.OnAccent), C(k.Success), C(k.Warning), C(k.Error), (byte)Math.Round(k.CardOpacity * 255));
        var detail = C(k.Detail); var fill = C(k.AccentFill);
        SetStops("GoldGradientBrush", [Lighten(detail, 0.35), detail, Darken(detail, 0.78)]);
        SetStops("PetalBrush", [Lighten(fill, 0.2), fill, Darken(fill, 0.45)]);
    }
    private void RestoreStops() { foreach (var (key, colors) in originalStops) SetStops(key, colors); }
    private void SetStops(string key, Color[] colors)
    {
        if (resources.TryGetValue(key, out var v) && v is LinearGradientBrush g)
            for (var i = 0; i < g.GradientStops.Count && i < colors.Length; i++) g.GradientStops[i].Color = colors[i];
    }

    private void ApplyHighContrast()
    {
        var bg = windows.GetColorValue(UIColorType.Background);
        var fg = windows.GetColorValue(UIColorType.Foreground);
        foreach (var key in new[] { "WindowBrush", "ShellBrush", "NavigationViewContentBackground", "PaneBrush", "NavigationViewDefaultPaneBackground", "NavigationViewExpandedPaneBackground",
            "CardBrush", "CardHoverBrush", "ElevatedBrush", "OnRoseBrush", "AccentSoftBrush", "BadgeBrush", "SubtleBrush", "NavigationViewItemBackgroundPointerOver", "NavigationViewItemBackgroundPressed" })
            Set(key, bg);
        foreach (var key in new[] { "CardBorderBrush", "NavigationViewContentGridBorderBrush", "DividerBrush", "CardHoverBorderBrush", "MainTextBrush", "MutedBrush", "RoseBrush", "GoldBrush",
            "SuccessBrush", "WarningBrush", "ErrorBrush", "NavigationViewItemForeground", "NavigationViewItemForegroundPointerOver", "NavigationViewItemForegroundSelected", "TopNavigationViewItemForeground",
            "TopNavigationViewItemForegroundPointerOver", "TopNavigationViewItemForegroundPressed", "TopNavigationViewItemForegroundSelected", "TopNavigationViewItemForegroundSelectedPointerOver",
            "TopNavigationViewItemForegroundSelectedPressed", "NavigationViewSelectionIndicatorForeground", "SystemControlFocusVisualPrimaryBrush", "BrandGoldBrush", "BrandRoseBrush" })
            Set(key, fg);
    }

    // Accent: (fill with white text, text color on dark surfaces, text color on light surfaces).
    private static (Color Fill, Color TextDark, Color TextLight) Accent(string id) => id switch
    {
        "violet" => (Rgb(0x6D, 0x43, 0xB5), Rgb(0xC2, 0xA5, 0xF5), Rgb(0x65, 0x40, 0xA0)),
        "blue" => (Rgb(0x24, 0x5C, 0xC1), Rgb(0x9C, 0xBD, 0xFF), Rgb(0x24, 0x5C, 0xC1)),
        "green" => (Rgb(0x28, 0x71, 0x5A), Rgb(0x8A, 0xD6, 0xB3), Rgb(0x24, 0x6C, 0x4D)),
        "crimson" => (Rgb(0xB0, 0x22, 0x36), Rgb(0xF0, 0x6A, 0x7A), Rgb(0x9E, 0x1E, 0x30)),
        "gold" => (Rgb(0x8F, 0x5E, 0x16), Rgb(0xEE, 0xC0, 0x7A), Rgb(0x7A, 0x4F, 0x12)),
        "dark" => (Rgb(0x3E, 0x32, 0x39), Rgb(0xF2, 0x7B, 0xA8), Rgb(0x8E, 0x24, 0x52)),
        _ => (Rgb(0xAD, 0x24, 0x5E), Rgb(0xF5, 0x8B, 0xB7), Rgb(0xB0, 0x19, 0x5A)),
    };

    private sealed record Tokens(Color Canvas, Color Shell, Color Card, Color Raised, Color Border, Color Divider, Color Text, Color TextSecondary)
    {
        public static readonly Tokens Dark = new(Rgb(0x10, 0x0D, 0x15), Rgb(0x17, 0x12, 0x1D), Rgb(0x21, 0x1A, 0x29), Rgb(0x2D, 0x23, 0x36),
            Rgb(0x36, 0x2C, 0x40), Rgb(0x2B, 0x23, 0x33), Rgb(0xF8, 0xF5, 0xFA), Rgb(0xC1, 0xB5, 0xCA));
        // Light "rosé porcelain": blush canvas, a rosier menu, warm off-white cards (no glaring pure white), rose borders,
        // deep plum text and strong rose accents — Lotus character with comfortable contrast.
        public static readonly Tokens Light = new(Rgb(0xF7, 0xEC, 0xF1), Rgb(0xF2, 0xE0, 0xE9), Rgb(0xFF, 0xFA, 0xFC), Rgb(0xF5, 0xDC, 0xE7),
            Rgb(0xE3, 0xBC, 0xCD), Rgb(0xEE, 0xD3, 0xDF), Rgb(0x2B, 0x10, 0x20), Rgb(0x6A, 0x3E, 0x55));
        public static readonly Tokens Graphite = new(Rgb(0x13, 0x13, 0x16), Rgb(0x19, 0x19, 0x1D), Rgb(0x22, 0x22, 0x28), Rgb(0x2D, 0x2D, 0x34),
            Rgb(0x38, 0x38, 0x40), Rgb(0x2A, 0x2A, 0x31), Rgb(0xF5, 0xF5, 0xF7), Rgb(0xBC, 0xBC, 0xC6));
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Color Darken(Color c, double f) => Rgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    private static Color Lighten(Color c, double f) => Rgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    private void Set(string key, Color color) { if (resources.TryGetValue(key, out var v) && v is SolidColorBrush b) b.Color = color; }
}
