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
    public static string ResolveLimiter(GamePresentationPrefs p, bool nvidia, string? api, bool frameGen) => p.Limiter == "auto"
        ? (frameGen && nvidia && (api is null or "Direct3D 11" or "Direct3D 12") ? "responsive" : "fluid") : p.Limiter;
    public static bool ReflexAvailable(bool nvidia, string? api) => nvidia && api is "Direct3D 11" or "Direct3D 12";
    public static double Cap(GamePresentationPrefs p, double realHz, bool vrr) => p.Cap > 0 ? p.Cap
        : vrr ? Math.Max(20, realHz - 3) : FramePacing.FixedCap(realHz);
}

// Serialized by the host, off the UI thread. Durable intent is written BEFORE a physical change.
// Restoration touches only a setting still owned by Lotus; external changes are preserved.
public sealed class FocusDisplaySession(IGameDisplayControl display, IDisplayRecoveryStore store)
{
    private DisplayRecovery? active;
    private string? rejected;
    public string Status { get; private set; } = "idle";
    public bool Active => active is not null;
    public bool Recover()
    {
        active ??= store.Load();
        return Restore();
    }
    public bool Update(string? gameKey, GamePresentationPrefs? prefs)
    {
        var key = gameKey is null || prefs is not { Enabled: true } ? null : gameKey + "|" + prefs;
        if (active is not null)
        {
            if (key == rejected && prefs is not null)
            {
                var now = display.Current(active.Applied.Device);
                if (now is not null && now.SameSetting(active.Applied)) return true;
            }
            if (!Restore()) return false;
        }
        if (key is null) { rejected = null; return true; }
        if (key == rejected) return Status == "already"; // do not fight a game/driver or retry a failed mode until focus changes
        rejected = key;
        if (prefs is null || !PresentationPlan.Valid(prefs)) { Status = "invalid"; return false; }
        var current = display.Current(prefs.Device);
        if (current is null || current.Width != prefs.Width || current.Height != prefs.Height) { Status = "resolution-changed"; return false; }
        var hz = PresentationPlan.ResolveHz(prefs, display.Modes());
        if (hz <= 0) { Status = "unsupported"; return false; }
        if (current.Hz == hz) { Status = "already"; return true; }
        var pending = new DisplayRecovery(current, current with { Hz = hz, ExactHz = 0 });
        store.Save(pending);
        active = pending;
        if (!display.SetHz(current, hz) || display.Current(current.Device) is not { } applied || !applied.SameSetting(pending.Applied))
        {
            Restore(); if (active is null) Status = "failed"; return false;
        }
        active = pending with { Applied = applied }; store.Save(active); Status = "active"; return true;
    }
    public bool Restore()
    {
        if (active is null) return true;
        var current = display.Current(active.Applied.Device);
        if (current is null) { Status = "restore-pending"; return false; }
        if (!current.SameSetting(active.Applied))
        {
            store.Save(null); active = null; Status = "external-change"; return true;
        }
        if (!display.SetHz(current, active.Before.Hz) || display.Current(current.Device) is not { } restored || !restored.SameSetting(active.Before))
        { Status = "restore-pending"; return false; }
        store.Save(null); active = null; Status = "restored"; return true;
    }
}
