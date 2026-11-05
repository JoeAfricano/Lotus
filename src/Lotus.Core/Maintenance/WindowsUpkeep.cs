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

public sealed partial class WindowsUpkeepViewModel(IWindowsUpkeep upkeep, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
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
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
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

    public void OpenWindowsUpdate() { try { upkeep.OpenWindowsUpdate(); } catch (Exception ex) { log(ex); } }
    public void OpenSystemRestore() { try { upkeep.OpenSystemRestore(); } catch (Exception ex) { log(ex); } }
}
