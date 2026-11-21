using System.Text.Json;
using Lotus.Core.Maintenance;

namespace Lotus.Core.Games;

// "Liberar memória ao abrir um jogo" (Perfil Jogo, once per game session, no administrator):
// 1. programs the player put in "Fechar antes de jogar" are asked to close normally (never forced);
// 2. Wallpaper Engine is paused through its own command line;
// 3. the working set of the player's idle programs goes back to Windows (their pages stay in the page file and come
//    back when the program is used again), never the game, the foreground window, Windows, drivers, anti-cheat or the
//    tools Lotus works with (the assistant "claude" is trimmed, never closed);
// 4. the memory available before and after is measured (GlobalMemoryStatusEx), nothing is estimated.
// When the game closes (or at the next start after a crash) Wallpaper Engine plays again and the closed programs
// reopen, minimized, if they are not open already.
public sealed record ReliefProcess(int Pid, string ExeName, string? Path, ProcessKind Kind, bool HasWindow, long Bytes = long.MaxValue);

public interface IMemoryReliefSystem
{
    long AvailableBytes();
    IReadOnlyList<ReliefProcess> Processes();
    int ForegroundPid();
    bool CloseGracefully(ReliefProcess process);
    // Bytes the working set went down by (0 when Windows refused).
    long Trim(ReliefProcess process);
    bool PauseWallpaper(string exePath);
    bool ResumeWallpaper(string exePath);
    bool IsRunning(string exeName);
    bool Reopen(string exePath);
    void Settle(TimeSpan time);
}

public sealed record MemoryReliefReport(long FreedBytes, long TrimmedBytes, IReadOnlyList<string> Closed, bool WallpaperPaused, int Trimmed);

public sealed record MemoryReliefUndo(List<string> Closed, string? Wallpaper);

public static class MemoryRelief
{
    public static readonly HashSet<string> WallpaperExes = new(StringComparer.OrdinalIgnoreCase) { "wallpaper64", "wallpaper32" };
    // Kept as they are even though they are "normal" programs: the game's launcher and overlay (Steam overlay lives in
    // steamwebhelper), voice and streaming (a call or a recording would stutter), and the shell's own helpers.
    public static readonly HashSet<string> NeverTrim = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "gameoverlayui", "gameoverlayui64", "EpicGamesLauncher", "EpicWebHelper", "EADesktop", "EABackgroundService", "GalaxyClient", "GalaxyClient Helper",
        "upc", "UbisoftConnect", "Battle.net", "XboxPcApp", "gamingservices", "Discord", "obs64", "obs32", "Streamlabs OBS", "explorer", "audiodg",
    };
    // Only programs holding at least this much are trimmed: small ones give little back and would only page in again
    // (the same idea as Process Lasso's SmartTrim, which only asks large background working sets to shrink).
    public const long TrimFrom = 64L << 20;
    // Shown only above this (a few MB moved around is noise, not a result).
    public const long ShownFrom = 100L << 20;

    // Who may be trimmed: the player's own programs, and the assistant (it may be running Lotus' own checks: it is
    // kept open, only its idle memory goes back). Never the game, the foreground window or Lotus itself.
    public static bool CanTrim(ReliefProcess p, int gamePid, int foregroundPid, int selfPid) =>
        p.Pid != gamePid && p.Pid != foregroundPid && p.Pid != selfPid && p.Pid > 4 && p.Bytes >= TrimFrom && !NeverTrim.Contains(p.ExeName)
        && (p.Kind == ProcessKind.Normal || (p.Kind == ProcessKind.Lotus && p.ExeName.Equals("claude", StringComparison.OrdinalIgnoreCase)));

    // Who may be closed: only what the player listed, only a normal program with a window (closing asks it like the X).
    public static bool CanClose(ReliefProcess p, IReadOnlyCollection<string> closeList, int gamePid) =>
        p.Kind == ProcessKind.Normal && p.HasWindow && p.Pid != gamePid && closeList.Contains(p.ExeName, StringComparer.OrdinalIgnoreCase)
        && !WallpaperExes.Contains(p.ExeName);
}

public sealed class MemoryReliefStep(IMemoryReliefSystem system, Func<IReadOnlyList<string>> closeList, Action<MemoryReliefReport>? report = null, int? selfPid = null,
    Func<IReadOnlyList<string>>? keepList = null) : IBoostStep
{
    public string Id => BoostSteps.Memory;
    private readonly int self = selfPid ?? Environment.ProcessId;

    public string? Apply(RunningGame game)
    {
        var before = system.AvailableBytes();
        var list = closeList();
        // "Nunca encolher" (Ferramentas > Memória e Processos): left exactly as they are, also never closed or paused.
        var keep = new HashSet<string>(keepList?.Invoke() ?? [], StringComparer.OrdinalIgnoreCase);
        var all = system.Processes().Where(p => !keep.Contains(p.ExeName)).ToList();
        var closed = new List<string>(); string? wallpaper = null;

        foreach (var group in all.Where(p => MemoryRelief.CanClose(p, list, game.Pid)).GroupBy(p => p.ExeName, StringComparer.OrdinalIgnoreCase))
        {
            var asked = false;
            foreach (var p in group) asked |= system.CloseGracefully(p);
            if (asked && group.Select(p => p.Path).FirstOrDefault(x => x is { Length: > 0 }) is { } path) closed.Add(path);
        }
        if (all.FirstOrDefault(p => MemoryRelief.WallpaperExes.Contains(p.ExeName) && p.Path is { Length: > 0 }) is { Path: { } wp } && system.PauseWallpaper(wp)) wallpaper = wp;

        var foreground = system.ForegroundPid();
        var closedNames = new HashSet<string>(closed.Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        long trimmed = 0; var count = 0;
        foreach (var p in all.Where(p => MemoryRelief.CanTrim(p, game.Pid, foreground, self) && !closedNames.Contains(p.ExeName)))
        {
            var down = system.Trim(p);
            if (down > 0) { trimmed += down; count++; }
        }
        system.Settle(TimeSpan.FromSeconds(2)); // closed programs exit and trimmed pages reach the standby list
        var freed = Math.Max(0, system.AvailableBytes() - before);
        report?.Invoke(new(freed, trimmed, closed, wallpaper is not null, count));
        // Always recorded (even with nothing to undo) so the session log shows the step ran.
        return JsonSerializer.Serialize(new MemoryReliefUndo(closed, wallpaper));
    }

    public bool Revert(string saved)
    {
        var undo = JsonSerializer.Deserialize<MemoryReliefUndo>(saved);
        if (undo is null) return false;
        var any = false;
        if (undo.Wallpaper is { Length: > 0 } wp) any |= system.ResumeWallpaper(wp);
        foreach (var path in undo.Closed ?? [])
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (system.IsRunning(name)) continue; // the player opened it again meanwhile
            any |= system.Reopen(path);
        }
        return any;
    }
}
