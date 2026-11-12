namespace Lotus.Core.Games;

// RivaTuner around playing: the Lotus overlay and the per-game frame limiter only work while RivaTuner is open, and on
// the player's PC it may not start with Windows. Lotus opens it right before the game (RivaTuner itself asks Windows
// for administrator), only when it is installed, closed and in use by Lotus. Nothing in RivaTuner's settings changes.
public sealed partial class GamesViewModel
{
    private int rtssStarting;
    // Shown on any screen when RivaTuner could not be opened (the game still opens).
    public event EventHandler<string>? RtssWarning;

    // true = opened now, false = needed but not opened, null = nothing to do.
    public async Task<bool?> EnsureRtssAsync(GameEntry game)
    {
        if (Rtss is not { } rtss) return null;
        try
        {
            if (!await Task.Run(() => NeedsRtss(rtss, game))) return null;
            if (Interlocked.Exchange(ref rtssStarting, 1) == 1) return null; // a double click never asks twice
            try
            {
                status = Text["RtssOpening"]; Notify();
                var ok = await rtss.StartAsync();
                status = ok ? "" : Text["RtssNotOpened"]; Notify();
                if (!ok) RtssWarning?.Invoke(this, Text["RtssNotOpened"]);
                return ok;
            }
            finally { Interlocked.Exchange(ref rtssStarting, 0); }
        }
        catch (Exception ex) { log(ex); return false; }
    }

    private bool NeedsRtss(Lotus.Core.Integrations.IRtssIntegration rtss, GameEntry game)
    {
        if (rtss.State().Installed != true || rtss.Running() != false) return false;
        if (rtss.OverlayConfigured() == true || rtss.LayoutActive()) return true;
        var exe = TechScanner?.Scan(game).MainExe ?? (game.ExePath is { Length: > 0 } path ? Path.GetFileName(path) : null);
        return exe is not null && rtss.LimiterMode(exe) is not null;
    }

    // The FPS limit RivaTuner applies to this game (its own profile, else Global), only while RivaTuner is open.
    private double? RtssCapFor(string executable)
    {
        if (Rtss is not { } rtss || rtss.Running() != true) return null;
        var exe = Path.GetFileName(executable);
        try { return rtss.LimiterCap(exe) ?? rtss.GlobalCap(); }
        catch (Exception ex) { log(ex); return null; }
    }
}
