namespace Lotus.Core.Games;

// "Escolha seu tipo de jogo" (José, 28/11/2026): three cards on the game's Otimizar screen.
// Modo PC = the screen's highest rate with RivaTuner just under it; Modo Console = 60 Hz with 59.99 (the existing Modo
// Console); Personalizado = the detailed Modo Console settings. "Desejo usar FrameGen neste jogo" changes what the cards
// describe (driver V-Sync + RivaTuner Reflex on NVIDIA). Choosing a card prepares the same per-game plan as before: the
// RivaTuner profile now (Windows asks for permission), the screen rate only while the game is in front.
public static class PlayModes
{
    public const string Pc = "pc", Console = "console", Custom = "custom";

    // The card the current plan matches, or null when none is set.
    public static string? Current(GamePlayPrefs prefs) => prefs.Prefer60 ? Console
        : prefs.Presentation is { Enabled: true } p ? (p.Hz == 0 && p.Limiter == "auto" && p.RequestedCap == 0 && prefs.PacingPreset == PacingPresets.Straight ? Pc : Custom)
        : null;

    // What a card will set: screen rate, RivaTuner cap and how frames are paced.
    public sealed record Plan(string Mode, int Hz, double Rate, double Cap, string Pacing);
    public static Plan For(string mode, int maxHz, double maxRate, bool frameGen, bool nvidia, bool vrr)
    {
        var pacing = !nvidia ? "front-edge" : frameGen && !vrr ? "vsync-reflex" : "reflex"; // Reflex is the default on NVIDIA
        if (mode == Console)
        {
            var hz = vrr ? maxHz : 60; var rate = vrr ? maxRate : 60;
            return new(mode, hz, rate, vrr ? GamesViewModel.ConsoleCap : FramePacing.FixedCap(60), pacing);
        }
        return new(mode, maxHz, maxRate, vrr ? Math.Max(20, maxRate - 3) : FramePacing.FixedCap(maxRate), pacing);
    }
}

public sealed partial class GamesViewModel
{
    // Modo PC: the highest rate of the screen the game runs on, RivaTuner just under it (automatic limiter).
    public async Task<bool> UsePcModeAsync(GameEntry game, LowLatencyInfo? info, bool adaptiveSync)
    {
        if (PlayPrefs is null || DisplayControl is null || profileSource is null) return false;
        if (PrefsFor(game).Prefer60 && !await SetConsole60Async(game, false, info, adaptiveSync)) return false;
        var pc = await Task.Run(profileSource.Read);
        var modes = await DisplayModesAsync();
        GameDisplayMode? output = null;
        foreach (var device in modes.Select(m => m.Device).Distinct())
            if (await Task.Run(() => DisplayControl.Current(device)) is { } c && c.Width == pc.Width && c.Height == pc.Height) { output = c; break; }
        if (output is null) { PresentationStatus = Text["GpUnsupported"]; return false; }
        if (PlayPrefs.Set(game.Key, PrefsFor(game) with { PacingPreset = PacingPresets.Straight }) is false) return false;
        return await SavePresentationAsync(game, new(true, output.Device, output.Width, output.Height, 0, "auto", 0), info, adaptiveSync);
    }

    public Task<bool> UseConsoleModeAsync(GameEntry game, LowLatencyInfo? info, bool adaptiveSync) => SetConsole60Async(game, true, info, adaptiveSync);

    // Highest rate of the output at the game's resolution (for the card texts); (0, 0) when unknown.
    public async Task<(int Hz, double Rate)> MaxRateAsync()
    {
        if (DisplayControl is null || profileSource is null) return (0, 0);
        var pc = await Task.Run(profileSource.Read);
        var modes = await DisplayModesAsync();
        var best = modes.Where(m => m.Width == pc.Width && m.Height == pc.Height).OrderByDescending(m => m.Hz).ThenByDescending(m => m.Rate).FirstOrDefault();
        return best is null ? (0, 0) : (best.Hz, best.Rate);
    }
}
