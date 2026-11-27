using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// ---- Windows em dia: pending restart/updates (read-only) and a System Restore point on request ----

// Uptime counts since the last real boot: with Fast Startup, "Shut down" does not reset it (only Restart does).
public sealed record WindowsStatus(bool UpdateRestart, bool ServicingRestart, bool FileRenames, TimeSpan Uptime, bool FastStartup);
// Created = Windows' list of restore points grew; Before/After are counts read in the same administrator session.
public sealed record RestorePointResult(bool Started, bool Created, int Before, int After, string? Error);

public interface IWindowsUpkeep
{
    WindowsStatus Read();
    Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken token = default); // UAC
    void OpenWindowsUpdate();
    void OpenSystemRestore();
}

public sealed partial class WindowsUpkeepViewModel(IWindowsUpkeep upkeep, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log, IMemoryCompression? memory = null) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public TaskState Work { get; } = new();
    public TaskState RestoreWork { get; } = new();
    [ObservableProperty] private string restartText = "";
    [ObservableProperty] private string restartAdvice = "";
    [ObservableProperty] private bool restartNeeded;
    [ObservableProperty] private string uptimeText = "";
    [ObservableProperty] private string restoreStatus = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool loaded;
    public WindowsStatus? Current { get; private set; }
    public bool CanAct => !Busy;
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanAct)); OnPropertyChanged(nameof(CanEnableMemory)); OnPropertyChanged(nameof(CanUndoMemory)); }
    // Memory compression + SysMain (read with the rest; changed only from its own buttons, after a confirmation).
    public TaskState MemoryWork { get; } = new();
    [ObservableProperty] private string memoryText = "";
    [ObservableProperty] private string memoryAdvice = "";
    [ObservableProperty] private string memoryStatus = "";
    [ObservableProperty] private bool memoryOn;
    public bool HasMemory => memory is not null && memoryState is not null;
    private MemoryCompressionStatus? memoryState;
    public bool CanEnableMemory => !Busy && memoryState is { } m && !MemoryCompressionRules.IsOn(m) && !m.RestartPending;
    public bool CanUndoMemory => !Busy && memoryState?.Saved is not null;
    public string TileStatus => !Loaded ? Text["WuTileHint"] : RestartNeeded ? Text["WuRestartShort"] : Text["WuOkShort"];
    partial void OnLoadedChanged(bool value) => OnPropertyChanged(nameof(TileStatus));
    partial void OnRestartNeededChanged(bool value) => OnPropertyChanged(nameof(TileStatus));
    public static readonly TimeSpan LongUptime = TimeSpan.FromDays(7);

    public async Task LoadAsync()
    {
        if (Busy) return;
        Busy = true; Work.Start();
        try
        {
            var s = await Task.Run(upkeep.Read);
            Current = s;
            RestartNeeded = s.UpdateRestart || s.ServicingRestart;
            RestartText = Text[s.UpdateRestart ? "WuRestartUpdate" : s.ServicingRestart ? "WuRestartServicing" : "WuNoRestart"];
            RestartAdvice = RestartNeeded ? Text["WuRestartAdvice"] : s.FileRenames ? Text["WuRenames"] : "";
            var days = (int)s.Uptime.TotalDays;
            UptimeText = Text.Format(days >= 1 ? "WuUptimeDays" : "WuUptimeHours", days, (int)s.Uptime.TotalHours)
                + (s.Uptime >= LongUptime ? " " + Text[s.FastStartup ? "WuUptimeFast" : "WuUptimeLong"] : "");
            await ReadMemory();
            Loaded = true; OnPropertyChanged(nameof(TileStatus));
            Work.Finish(Text["SyRead"]);
        }
        catch (Exception ex) { log(ex); RestartText = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }

    // Asked in the UI first. The result is what Windows' own restore point list says afterwards.
    public async Task CreateRestorePointAsync()
    {
        if (Busy) return;
        Busy = true; RestoreWork.Start(); RestoreStatus = Text["WuRestoreWorking"];
        try
        {
            var r = await upkeep.CreateRestorePointAsync("Lotus " + DateTime.Now.ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Text.Language)));
            RestoreStatus = !r.Started ? Text["WuRestoreDeclined"]
                : r.Created ? Text.Format("WuRestoreCreated", r.After)
                : r.Error is { Length: > 0 } ? Text.Format("WuRestoreError", r.Error)
                : Text["WuRestoreSkipped"]; // Windows allows one automatic point per 24 h, or protection is off
            journal.Add(new(DateTimeOffset.Now, "windows", "restore-point", $"{r.Before} -> {r.After}" + (r.Error is null ? "" : " " + r.Error), r.Created));
            if (r.Created) RestoreWork.Finish(Text["WuRestoreDone"]); else RestoreWork.Fail();
        }
        catch (Exception ex) { log(ex); RestoreStatus = Text["ToolFailed"]; RestoreWork.Fail(); }
        finally { Busy = false; }
    }

    private async Task ReadMemory()
    {
        if (memory is null) return;
        try { memoryState = await Task.Run(memory.Read); }
        catch (Exception ex) { log(ex); memoryState = null; }
        if (memoryState is { } m)
        {
            MemoryOn = MemoryCompressionRules.IsOn(m);
            MemoryText = m.RestartPending ? Text["MzRestartPending"]
                : Text.Format(MemoryOn ? "MzOn" : "MzOff", Text[m.Active ? "MzStateOn" : "MzStateOff"], Text["MzSysMain" + m.SysMain] + (m.SysMainRunning ? "" : " " + Text["MzStopped"]));
            MemoryAdvice = m.RestartPending ? "" : MemoryOn ? Text["MzOnAdvice"] : Text[MemoryCompressionRules.Recommended(m) ? "MzOffAdviceSmall" : "MzOffAdvice"];
        }
        foreach (var n in new[] { nameof(HasMemory), nameof(CanEnableMemory), nameof(CanUndoMemory) }) OnPropertyChanged(n);
    }

    // Asked in the UI first (administrator + restart). The result is what Windows reports afterwards.
    public Task EnableMemoryAsync() => ChangeMemory(undo: false);
    public Task UndoMemoryAsync() => ChangeMemory(undo: true);
    private async Task ChangeMemory(bool undo)
    {
        if (Busy || memory is null) return;
        Busy = true; MemoryWork.Start(); MemoryStatus = Text["MzWorking"];
        try
        {
            var r = await (undo ? memory.UndoAsync() : memory.EnableAsync());
            MemoryStatus = !r.Started ? Text["MzDeclined"] : r.Ok ? Text[undo ? "MzUndone" : "MzEnabled"] : Text.Format("MzError", r.Error ?? "?");
            journal.Add(new(DateTimeOffset.Now, "windows", undo ? "undo:memory-compression" : "apply:memory-compression", r.Error ?? "", r.Ok));
            await ReadMemory();
            if (r.Ok) MemoryWork.Finish(Text["MzDone"]); else MemoryWork.Fail();
        }
        catch (Exception ex) { log(ex); MemoryStatus = Text["ToolFailed"]; MemoryWork.Fail(); }
        finally { Busy = false; }
    }

    public void OpenWindowsUpdate() { try { upkeep.OpenWindowsUpdate(); } catch (Exception ex) { log(ex); } }
    public void OpenSystemRestore() { try { upkeep.OpenSystemRestore(); } catch (Exception ex) { log(ex); } }
}
