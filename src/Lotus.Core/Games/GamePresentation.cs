namespace Lotus.Core.Games;

// Opt-in, per-game. Width/height are a guard, never a request to change resolution.
public sealed record GamePresentationPrefs(bool Enabled = false, string Device = "", int Width = 0, int Height = 0,
    int Hz = 0, string Limiter = "auto", double Cap = 0, string Exe = "", string AppliedLimiter = "", double PreparedRate = 0, double RequestedCap = 0);
public sealed record GameDisplayMode(string Device, string Name, int Width, int Height, int Hz, double ExactHz = 0)
{
    public double Rate => ExactHz > 0 ? ExactHz : Hz;
    public bool SameOutput(GameDisplayMode other) => Device == other.Device && Width == other.Width && Height == other.Height;
    public bool SameSetting(GameDisplayMode other) => SameOutput(other) && Hz == other.Hz
        && (ExactHz <= 0 || other.ExactHz <= 0 || Math.Abs(ExactHz-other.ExactHz)<0.02);
}
public interface IGameDisplayControl
{
    IReadOnlyList<GameDisplayMode> Modes();
    GameDisplayMode? Current(string device);
    // Only frequency may change; fails if output/resolution no longer match.
    bool SetHz(GameDisplayMode expected, int hz);
}
public sealed record DisplayRecovery(GameDisplayMode Before, GameDisplayMode Applied);
public interface IDisplayRecoveryStore { DisplayRecovery? Load(); void Save(DisplayRecovery? recovery); }

// Collapse rapid focus notifications before touching the display driver.
public sealed class FocusTransitionGate(Func<TimeSpan,Task>? wait = null)
{
    private int revision;
    public int Next() => Interlocked.Increment(ref revision);
    public bool IsCurrent(int ticket) => ticket == Volatile.Read(ref revision);
    public async Task<bool> WaitAsync(int ticket)
    {
        await (wait?.Invoke(TimeSpan.FromMilliseconds(200)) ?? Task.Delay(200));
        return IsCurrent(ticket);
    }
}

public static class PresentationPlan
{
    public static bool Valid(GamePresentationPrefs p) => p.Width > 0 && p.Height > 0 && p.Device.Length > 0
        && p.Hz >= 0 && p.Cap >= 0 && double.IsFinite(p.Cap)
        && p.Limiter is "auto" or "fluid" or "responsive" or "async" or "back";
    public static IReadOnlyList<GameDisplayMode> ForOutput(IEnumerable<GameDisplayMode> modes, GameDisplayMode current) =>
        modes.Where(m => m.SameOutput(current)).DistinctBy(m => m.Hz).OrderBy(m => m.Hz).ToArray();
    public static int ResolveHz(GamePresentationPrefs p, IReadOnlyList<GameDisplayMode> modes) =>
        modes.Where(m => m.Device == p.Device && m.Width == p.Width && m.Height == p.Height && (p.Hz == 0 || m.Hz == p.Hz))
            .Select(m => m.Hz).DefaultIfEmpty(0).Max();
    // Frame generation on NVIDIA: with VRR, Reflex; on a fixed-refresh monitor, "fgvsync" (driver V-Sync + RivaTuner
    // Reflex). Gothic 1 Remake at 75 Hz (22/11/2026): smooth with no tearing while walking, while Reflex without V-Sync showed
    // frames in pairs with tearing and scanline sync was smooth at 105 ms. See docs/LOTUS_FRAMEGEN_NO_VRR.md.
    // Default (José, 28/11/2026, after the study): RivaTuner's Reflex limiter on NVIDIA (Direct3D 11/12 or not known yet),
    // front edge sync on other cards. A measured choice per game (LimiterTrial) or a mode picked by hand still wins.
    public static string ResolveLimiter(GamePresentationPrefs p, bool nvidia, string? api, bool frameGen, bool vrr = false)
    {
        if (p.Limiter != "auto") return p.Limiter;
        var reflex = nvidia && api is null or "Direct3D 11" or "Direct3D 12";
        if (!reflex) return "fluid";
        return frameGen && !vrr ? "fgvsync" : "responsive";
    }
    // The tray icon says "Modo Console não está ativo" in these states: the switch was needed and did not happen, or it was
    // given up because something kept changing the rate. A game already at its Hz is not a failure.
    public static bool ConsoleModeShouldBeOn(string state, bool switchNeeded) => state is "contested" or "rtss-closed"
        || (switchNeeded && state is "failed" or "rtss-changed" or "api-unsupported" or "prepare-again");
    public static bool ReflexAvailable(bool nvidia, string? api) => nvidia && api is "Direct3D 11" or "Direct3D 12";
    public static double Cap(GamePresentationPrefs p, double realHz, bool vrr) => p.Cap > 0 ? p.Cap
        : vrr ? Math.Max(20, realHz - 3) : FramePacing.FixedCap(realHz);
}

// After the switch, the real rate is checked for ~10 s (Gothic 1 Remake, 27/11/2026: twice the screen stayed at 74 Hz and the
// game repeated 1 frame in 4, with no trace in the log). A rate that did not stick or went back is asked again at most
// MaxRetries times; if something keeps changing it back, Lotus stops (no fight) and says so.
public enum RateCheck { Wait, Done, Retry, GiveUp }
public sealed class RateGuard(int maxRetries = RateGuard.MaxRetries, TimeSpan? window = null)
{
    public const int MaxRetries = 3;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private readonly TimeSpan span = window ?? Window;
    private GameDisplayMode? target;
    private DateTimeOffset since;
    public int Retries { get; private set; }
    public bool Watching => target is not null;
    public void Start(GameDisplayMode applied, DateTimeOffset now) { target = applied; since = now; Retries = 0; }
    public void Stop() => target = null;
    public RateCheck Check(GameDisplayMode? current, DateTimeOffset now)
    {
        if (target is null) return RateCheck.Done;
        if (current is not null && current.SameOutput(target) && current.Hz == target.Hz)
        {
            if (now - since < span) return RateCheck.Wait;
            target = null; return RateCheck.Done; // held for the whole window
        }
        if (Retries >= maxRetries) { target = null; return RateCheck.GiveUp; }
        Retries++; since = now; // the window starts again after each new request
        return RateCheck.Retry;
    }
}

// Serialized by the host, off the UI thread. Durable intent is written BEFORE a physical change.
// Restoration touches only a setting still owned by Lotus; external changes are preserved.
// Every change is traced (requested, obtained, undone and why, with the exact Hz) for the log.
public sealed class FocusDisplaySession(IGameDisplayControl display, IDisplayRecoveryStore store, Action<string>? trace = null)
{
    private DisplayRecovery? active;
    private string? rejected;
    public string Status { get; private set; } = "idle";
    public bool Active => active is not null;
    public GameDisplayMode? Applied => active?.Applied;
    private static string Hz(GameDisplayMode? m) => m is null ? "unknown" : m.Rate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " Hz";
    private void Trace(string line) => trace?.Invoke("Modo Console: " + line);
    public bool Recover()
    {
        active ??= store.Load();
        if (active is not null) Trace($"recovering an unfinished switch ({Hz(active.Applied)} back to {Hz(active.Before)})");
        return Restore("recovery");
    }

    // The rate went back (driver, game or another app) while Lotus still owns the switch: ask once more and verify.
    public bool Reassert()
    {
        if (active is null) return false;
        var current = display.Current(active.Applied.Device);
        if (current is null) { Trace("display not found while checking the rate"); return false; }
        if (current.SameOutput(active.Applied) && current.Hz == active.Applied.Hz) return true;
        if (!current.SameOutput(active.Applied)) { Trace($"resolution changed to {current.Width}x{current.Height}; switch released"); Release("resolution-changed"); return false; }
        Trace($"rate is {Hz(current)}, expected {Hz(active.Applied)}: asking {active.Applied.Hz} Hz again");
        var ok = display.SetHz(current, active.Applied.Hz) && display.Current(current.Device) is { } now && now.SameOutput(active.Applied) && now.Hz == active.Applied.Hz;
        Trace(ok ? $"rate back at {Hz(display.Current(current.Device))}" : $"switch did not take; rate is {Hz(display.Current(current.Device))}");
        if (ok) { active = active with { Applied = display.Current(current.Device)! }; store.Save(active); }
        return ok;
    }

    // Something keeps changing the rate: Lotus stops owning it and leaves the screen as it is (never a fight).
    public void Release(string status)
    {
        if (active is null) return;
        Trace($"stopped: {status}; screen left at {Hz(display.Current(active.Applied.Device))}, it was {Hz(active.Before)} before the game");
        store.Save(null); active = null; Status = status;
    }
    public bool Update(string? gameKey, GamePresentationPrefs? prefs, string reason = "focus left the game")
    {
        var key = gameKey is null || prefs is not { Enabled: true } ? null : gameKey + "|" + prefs;
        if (active is not null)
        {
            if (key == rejected && prefs is not null)
            {
                var now = display.Current(active.Applied.Device);
                if (now is not null && now.SameSetting(active.Applied)) return true;
            }
            if (!Restore(key is null ? reason : "another game or setting")) return false;
        }
        if (key is null) { rejected = null; return true; }
        if (key == rejected) return Status == "already"; // do not fight a game/driver or retry a failed mode until focus changes
        rejected = key;
        if (prefs is null || !PresentationPlan.Valid(prefs)) { Status = "invalid"; Trace($"{gameKey}: saved settings are not valid; nothing changed"); return false; }
        var current = display.Current(prefs.Device);
        if (current is null || current.Width != prefs.Width || current.Height != prefs.Height)
        { Status = "resolution-changed"; Trace($"{gameKey}: screen is {(current is null ? "not found" : $"{current.Width}x{current.Height}")}, prepared for {prefs.Width}x{prefs.Height}; nothing changed"); return false; }
        var hz = PresentationPlan.ResolveHz(prefs, display.Modes());
        if (hz <= 0) { Status = "unsupported"; Trace($"{gameKey}: {prefs.Hz} Hz is not offered by the screen; nothing changed"); return false; }
        if (current.Hz == hz) { Status = "already"; Trace($"{gameKey}: screen already at {Hz(current)}"); return true; }
        var pending = new DisplayRecovery(current, current with { Hz = hz, ExactHz = 0 });
        store.Save(pending);
        active = pending;
        Trace($"{gameKey}: requested {hz} Hz (screen at {Hz(current)})");
        if (!display.SetHz(current, hz) || display.Current(current.Device) is not { } applied || !applied.SameSetting(pending.Applied))
        {
            Trace($"{gameKey}: switch to {hz} Hz did not take; screen at {Hz(display.Current(current.Device))}");
            Restore("switch failed"); if (active is null) Status = "failed"; return false;
        }
        active = pending with { Applied = applied }; store.Save(active); Status = "active";
        Trace($"{gameKey}: screen now at {Hz(applied)}");
        return true;
    }
    public bool Restore(string reason = "requested")
    {
        if (active is null) return true;
        var current = display.Current(active.Applied.Device);
        if (current is null) { Status = "restore-pending"; Trace($"restore waits ({reason}): display not found"); return false; }
        if (!current.SameSetting(active.Applied))
        {
            Trace($"not restored ({reason}): screen is at {Hz(current)}, someone else changed it; left as it is");
            store.Save(null); active = null; Status = "external-change"; return true;
        }
        if (!display.SetHz(current, active.Before.Hz) || display.Current(current.Device) is not { } restored || !restored.SameSetting(active.Before))
        { Status = "restore-pending"; Trace($"restore to {Hz(active.Before)} did not take ({reason})"); return false; }
        Trace($"restored to {Hz(restored)} ({reason})");
        store.Save(null); active = null; Status = "restored"; return true;
    }
}
