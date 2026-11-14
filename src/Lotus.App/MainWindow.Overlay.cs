using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App;

// Generated overlay layouts (Lotus Lateral, Lotus Pulse) follow the version and, for Pulse, the active theme: the
// exported copy in %LOCALAPPDATA%\Lotus\Overlays is renewed in the background when it differs (RivaTuner reads it the next
// time it starts; Lotus does not restart it for that). The player's own layouts (Top Sombra, Neon) are never rewritten.
public sealed partial class MainWindow
{
    private int overlayRefreshQueued;

    private void RefreshGeneratedOverlays()
    {
        Lotus.Infrastructure.Integrations.LotusOverlayLayout.PulseColors = PulseColors();
        if (Interlocked.Exchange(ref overlayRefreshQueued, 1) == 1) return; // a refresh is already on its way (theme previews fire often)
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Interlocked.Exchange(ref overlayRefreshQueued, 0);
            if (Lotus.Infrastructure.Integrations.LotusOverlayLayout.RefreshGenerated()) Serilog.Log.Information("Overlay: generated layouts renewed");
        });
    }

    // The overlay sits on a dark translucent panel over the game: bright accent and detail colors read best there, so a
    // light theme keeps the Lotus defaults and a custom theme lends its accent fill and detail color.
    private static Lotus.Infrastructure.Integrations.LotusPulseLayout.Colors PulseColors()
    {
        var d = Lotus.Infrastructure.Integrations.LotusPulseLayout.Default;
        if (ThemePalette.Element == ElementTheme.Light) return d;
        string Hex(string key, string fallback) =>
            Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush b ? $"{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}" : fallback;
        if (ThemePalette.Instance?.CustomTokens is { } ct)
            return d with { Accent = Lotus.Infrastructure.Integrations.LotusPulseLayout.Hex(ct.AccentText.ToString(), d.Accent), Detail = Lotus.Infrastructure.Integrations.LotusPulseLayout.Hex(ct.Detail.ToString(), d.Detail) };
        return d with { Accent = Hex("RoseBrush", d.Accent), Detail = Hex("GoldBrush", d.Detail) };
    }
}
