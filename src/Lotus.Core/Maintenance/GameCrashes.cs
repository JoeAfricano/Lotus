using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// ---- "Por que meu jogo fechou?": game crashes/hangs from Windows' logs, matched with what happened at the same time ----

public enum CrashKind { Crash, Hang }
// App = the program that closed (from the Application log); Module = the part that failed, as Windows recorded it.
public sealed record AppCrash(DateTime When, CrashKind Kind, string App, string? AppPath, string? Module, string? ExceptionCode);
// System events around a crash: GPU driver reset, unexpected shutdown, out of memory, hardware error, disk error.
public enum SystemSignal { GpuReset, PowerLoss, OutOfMemory, HardwareError, DiskError }
public sealed record SignalEvent(DateTime When, SystemSignal Signal, string Source);

public interface ICrashLog
{
    IReadOnlyList<AppCrash> Crashes(TimeSpan since);
    IReadOnlyList<SignalEvent> Signals(TimeSpan since);
}

// Plain-language causes, most specific first. "Likely" is a hint from what Windows recorded, never a certainty.
public enum CrashCause { GpuDriver, Overlay, OutOfMemory, PowerLoss, Hardware, Disk, Hang, GraphicsLibrary, Runtime, Game }

public static class CrashRules
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
    private static readonly string[] GpuModules = ["nvwgf2umx.dll", "nvlddmkm.sys", "nvoglv64.dll", "nvd3dumx.dll", "nvgpucomp64.dll", "amdxx64.dll", "atidxx64.dll", "amdvlk64.dll", "atiumd64.dll", "igd10iumd64.dll", "igd12umd64.dll", "igxelpicd64.dll"];
    private static readonly string[] OverlayModules = ["GameOverlayRenderer64.dll", "GameOverlayRenderer.dll", "DiscordHook64.dll", "RTSSHooks64.dll", "RTSSHooks.dll", "owclient.dll", "ow-graphics-vulkan.dll", "MedalOverlay.dll", "nvspcap64.dll", "XboxGameBarHook.dll", "EOSOVH-Win64-Shipping.dll"];
    private static readonly string[] GraphicsModules = ["d3d11.dll", "d3d12.dll", "d3d12core.dll", "dxgi.dll", "vulkan-1.dll", "d3d9.dll", "opengl32.dll"];
    private static readonly string[] RuntimeModules = ["clr.dll", "coreclr.dll", "mono-2.0-bdwgc.dll", "UnityPlayer.dll", "jvm.dll", "KERNELBASE.dll"];

    public static CrashCause Cause(AppCrash crash, IReadOnlyList<SignalEvent> signals)
    {
        var near = signals.Where(s => (s.When - crash.When).Duration() <= Window).Select(s => s.Signal).ToHashSet();
        var module = crash.Module ?? "";
        bool Is(string[] list) => list.Any(m => module.Equals(m, StringComparison.OrdinalIgnoreCase));
        if (near.Contains(SystemSignal.GpuReset) || Is(GpuModules)) return CrashCause.GpuDriver;
        if (near.Contains(SystemSignal.HardwareError)) return CrashCause.Hardware;
        if (near.Contains(SystemSignal.OutOfMemory)) return CrashCause.OutOfMemory;
        if (Is(OverlayModules)) return CrashCause.Overlay;
        if (near.Contains(SystemSignal.DiskError)) return CrashCause.Disk;
        if (crash.Kind == CrashKind.Hang) return CrashCause.Hang;
        if (Is(GraphicsModules)) return CrashCause.GraphicsLibrary;
        if (Is(RuntimeModules) || crash.ExceptionCode is "e0434352" or "0xe0434352") return CrashCause.Runtime;
        return CrashCause.Game;
    }

    // A game = one of the library's executables, or a program inside a game/launcher folder.
    public static bool IsGame(AppCrash crash, IReadOnlyCollection<string> gameExes)
    {
        if (crash.AppPath is { Length: > 0 } path)
        {
            if (gameExes.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
            return ProcessRules.Classify(Path.GetFileNameWithoutExtension(path), path) == ProcessKind.Game;
        }
        return gameExes.Any(g => Path.GetFileName(g).Equals(crash.App, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record CrashRow(string Title, string When, string CauseTitle, string Advice, string Technical, bool Serious);

public sealed partial class GameCrashViewModel(ICrashLog crashes, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<CrashRow> Rows { get; } = [];
    public TaskState Work { get; } = new();
    public Func<IReadOnlyCollection<string>>? GameExes { get; set; }
    public Func<string, string?>? GameName { get; set; } // exe path → the name shown in Meus Jogos
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool loaded;
    public bool CanAct => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    public string TileStatus => Summary.Length > 0 ? Summary : Text["CrTileHint"];
    partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(TileStatus));
    public int Count => Rows.Count;
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);

    public async Task LoadAsync(int days = 14)
    {
        if (Busy) return;
        Busy = true; Work.Start();
        try
        {
            var since = TimeSpan.FromDays(days);
            var (list, signals) = await Task.Run(() => (crashes.Crashes(since), crashes.Signals(since)));
            var games = GameExes?.Invoke() ?? [];
            Rows.Clear();
            foreach (var c in list.Where(c => CrashRules.IsGame(c, games)).OrderByDescending(c => c.When).Take(15))
            {
                var cause = CrashRules.Cause(c, signals);
                var name = (c.AppPath is { } p ? GameName?.Invoke(p) : null) ?? Path.GetFileNameWithoutExtension(c.App);
                var technical = Text.Format("CrTechnical", c.App, c.Module ?? "?", c.ExceptionCode ?? "-");
                Rows.Add(new(name, c.When.ToString("g", Culture), Text["CrCause" + cause], Text["CrAdvice" + cause], technical, cause is CrashCause.PowerLoss or CrashCause.Hardware or CrashCause.Disk));
            }
            // Unexpected shutdowns matter even without a game crash record (the PC itself went down).
            var power = signals.Count(s => s.Signal == SystemSignal.PowerLoss);
            Summary = Rows.Count == 0 ? Text.Format("CrNone", days) : Text.Format(Rows.Count == 1 ? "CrFoundOne" : "CrFound", Rows.Count, days);
            Status = Summary + (power > 0 ? " " + Text.Format("CrPowerLoss", power) : "");
            Loaded = true; OnPropertyChanged(nameof(Count));
            Work.Finish(Text["SyRead"]);
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }
}
