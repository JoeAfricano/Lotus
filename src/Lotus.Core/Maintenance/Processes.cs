using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// ---- Memória e Processos: real RAM and CPU per program, closing only what the player confirms ----

// System = Windows itself; AntiCheat = anti-cheat services (closing them breaks online games); Game = a running game;
// Lotus = Lotus and the tools it works with (RTSS, Afterburner, PresentMon). None of these can be closed from here.
public enum ProcessKind { Normal, System, AntiCheat, Game, Lotus }

public sealed record ProcessSample(string Name, string ExeName, string? Path, long Bytes, double CpuPercent, bool HasWindow, IReadOnlyList<int> Pids, ProcessKind Kind);

public interface IProcessSampler
{
    // Two readings about a second apart, grouped by program; CPU is the share of the whole processor.
    Task<IReadOnlyList<ProcessSample>> SampleAsync(CancellationToken token = default);
    // Asks the windows to close normally (like clicking X). Returns how many were asked.
    int CloseGracefully(ProcessSample app);
    // Ends the processes (unsaved work is lost). Never for protected kinds; returns how many ended.
    int ForceEnd(ProcessSample app);
}

// Programs the player chose to close before playing (by .exe name), kept in a small file.
public interface ICloseListStore { IReadOnlyList<string> Load(); void Save(IReadOnlyList<string> exeNames); }
public sealed class MemoryCloseList : ICloseListStore
{
    private List<string> items = [];
    public IReadOnlyList<string> Load() => items;
    public void Save(IReadOnlyList<string> exeNames) => items = [.. exeNames];
}

public static class ProcessRules
{
    public static readonly HashSet<string> System = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "dwm", "smss",
        "fontdrvhost", "explorer", "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "SearchIndexer", "TextInputHost", "RuntimeBroker", "sihost", "ctfmon", "audiodg", "conhost", "spoolsv", "taskhostw", "dllhost",
        "WmiPrvSE", "LsaIso", "ApplicationFrameHost", "SystemSettings", "SgrmBroker", "MpDefenderCoreService", "WUDFHost", "atiesrxx", "atieclxx", "nvcontainer", "NVDisplay.Container",
    };
    public static readonly HashSet<string> AntiCheat = new(StringComparer.OrdinalIgnoreCase)
    {
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEService_x64", "vgc", "vgtray", "faceit", "FACEITService", "FaceitAC", "EAAntiCheat.GameServiceLauncher",
        "EAAntiCheat.GameService", "PnkBstrA", "PnkBstrB", "xigncode", "nProtect", "GameGuard", "mhyprot", "ACE-Guard", "ricochet", "EQUAntiCheat",
    };
    public static readonly HashSet<string> LotusFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lotus.App", "RTSS", "RTSSHooksLoader64", "MSIAfterburner", "PresentMon", "PresentMon-x64", "EncoderServer", "EncoderServer64", "claude", // the assistant that may be running Lotus' own checks
    };
    // Folders where games live (any launcher); a process from there counts as a game.
    private static readonly string[] GameFolders = [@"\steamapps\common\", @"\Epic Games\", @"\GOG Galaxy\Games\", @"\XboxGames\", @"\Riot Games\", @"\Ubisoft Game Launcher\games\", @"\EA Games\", @"\Battle.net\"];

    public static ProcessKind Classify(string exeName, string? path, IReadOnlyCollection<string>? knownGameExes = null)
    {
        if (LotusFamily.Contains(exeName)) return ProcessKind.Lotus;
        if (AntiCheat.Contains(exeName)) return ProcessKind.AntiCheat;
        if (System.Contains(exeName)) return ProcessKind.System;
        if (path is { Length: > 0 })
        {
            if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase)) return ProcessKind.System;
            if (GameFolders.Any(g => path.Contains(g, StringComparison.OrdinalIgnoreCase))) return ProcessKind.Game;
            if (knownGameExes?.Contains(path) == true) return ProcessKind.Game;
        }
        return ProcessKind.Normal;
    }
}

public sealed partial class ProcessRow(ProcessSample sample, string sizeText, string cpuText, string kindText, bool inList) : ObservableObject
{
    public ProcessSample Sample { get; } = sample;
    public string Name => Sample.Name;
    public string Location => Sample.Path ?? Sample.ExeName + ".exe";
    public string SizeText { get; } = sizeText;
    public string CpuText { get; } = cpuText;
    public string KindText { get; } = kindText;
    public bool CanClose => Sample.Kind == ProcessKind.Normal;
    public bool Protected => !CanClose;
    [ObservableProperty] private bool selected;
    [ObservableProperty] private bool inCloseList = inList;
}

public sealed partial class ProcessesViewModel(IProcessSampler sampler, ICloseListStore closeList, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<ProcessRow> Rows { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool showAll;   // false: only programs worth looking at (≥ 100 MB or ≥ 1% CPU, or in the list)
    public Func<IReadOnlyCollection<string>>? GameExes { get; set; }
    public string TileStatus => Summary.Length > 0 ? Summary : Text["PrTileHint"];
    partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(TileStatus));
    partial void OnShowAllChanged(bool value) => Fill();
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanAct)); }
    public bool CanAct => !Busy;
    private IReadOnlyList<ProcessSample> last = [];
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    public string Size(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Culture) + " GB" : (bytes / (double)(1L << 20)).ToString("0", Culture) + " MB";
    public IReadOnlyList<string> CloseList => closeList.Load();

    public async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true; Work.Start(); Status = Text["PrReading"];
        try
        {
            Work.Report(30);
            last = await sampler.SampleAsync();
            Fill();
            var total = last.Sum(p => p.Bytes);
            var top = last.Where(p => p.Kind == ProcessKind.Normal).MaxBy(p => p.Bytes);
            Summary = top is null ? Text.Format("PrSummaryNone", Size(total)) : Text.Format("PrSummary", Size(total), top.Name, Size(top.Bytes));
            Status = Text["PrPick"];
            Work.Finish(Text["PrRead"]);
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }

    private void Fill()
    {
        var list = closeList.Load();
        Rows.Clear();
        foreach (var p in last.OrderByDescending(p => p.Bytes))
        {
            var listed = list.Contains(p.ExeName, StringComparer.OrdinalIgnoreCase);
            if (!ShowAll && p.Bytes < 100L << 20 && p.CpuPercent < 1 && !listed) continue;
            Rows.Add(new(p, Size(p.Bytes), p.CpuPercent.ToString("0.0", Culture) + "%", Text["PrKind" + p.Kind], listed));
        }
    }

    public IReadOnlyList<ProcessRow> Chosen => [.. Rows.Where(r => r.Selected && r.CanClose)];

    // Normal close first (unsaved work prompts stay with the app); what is left is measured again, not assumed.
    public async Task CloseSelectedAsync(bool force = false)
    {
        var chosen = Chosen;
        if (Busy || chosen.Count == 0) return;
        Busy = true; Work.Start(); Status = Text["PrClosing"];
        try
        {
            var before = chosen.Sum(r => r.Sample.Bytes); var asked = 0;
            for (var i = 0; i < chosen.Count; i++)
            {
                var s = chosen[i].Sample;
                if (s.Kind != ProcessKind.Normal) continue; // checked again: never system, anti-cheat, games or Lotus
                asked += await Task.Run(() => force ? sampler.ForceEnd(s) : sampler.CloseGracefully(s));
                Work.Report((i + 1) * 60.0 / chosen.Count);
            }
            await Task.Delay(1500); Work.Report(80);
            last = await sampler.SampleAsync();
            var still = last.Where(p => chosen.Any(c => c.Sample.ExeName.Equals(p.ExeName, StringComparison.OrdinalIgnoreCase))).ToList();
            var freed = Math.Max(0, before - still.Sum(p => p.Bytes));
            Fill();
            Status = asked == 0 ? Text["PrNothing"] : still.Count == 0 ? Text.Format("PrClosed", Size(freed)) : Text.Format("PrClosedSome", Size(freed), string.Join(", ", still.Select(s => s.Name)));
            journal.Add(new(DateTimeOffset.Now, "processes", force ? "force-end" : "close", $"{string.Join(", ", chosen.Select(c => c.Sample.ExeName))}: {freed} bytes freed, {still.Count} still open", still.Count == 0));
            if (asked == 0) Work.Fail(); else Work.Finish(Text.Format("PrDone", Size(freed)));
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }

    // The player's list: ticks the listed programs that are open (closing still asks).
    public int SelectCloseList()
    {
        var list = closeList.Load(); var n = 0;
        foreach (var r in Rows) { r.Selected = r.CanClose && list.Contains(r.Sample.ExeName, StringComparer.OrdinalIgnoreCase); if (r.Selected) n++; }
        return n;
    }
    public void ToggleInList(ProcessRow row)
    {
        if (!row.CanClose) return;
        var list = closeList.Load().ToList();
        if (list.RemoveAll(x => x.Equals(row.Sample.ExeName, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(row.Sample.ExeName);
        try { closeList.Save(list); row.InCloseList = !row.InCloseList; } catch (Exception ex) { log(ex); }
    }
}
