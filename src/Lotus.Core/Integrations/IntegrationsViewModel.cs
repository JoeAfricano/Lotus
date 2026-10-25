using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Integrations;

// Complements Lotus can use on the player's behalf: SCSKiller (shaders), RivaTuner (overlay/limiter), MSI Afterburner (sensors).
public sealed record ToolState(bool Installed, string? Version);
public interface IRtssIntegration
{
    ToolState State();
    bool? OverlayConfigured();           // null = profile unreadable
    bool HasBackup();
    Task<bool> ApplyLotusOverlayAsync(); // elevated helper; true only when verified by reading the profile back
    Task<bool> RestoreAsync();
    // Full Lotus overlay (OverlayEditor layout): export without admin, activate/restore through the elevated helper.
    string ExportLayout();
    bool LayoutActive();
    bool HasLayoutBackup();
    Task<bool> ActivateLayoutAsync();
    Task<bool> RestoreLayoutAsync();
}
public interface IToolDetector { ToolState Afterburner(); ToolState ScsKiller(); }

public sealed class IntegrationsViewModel(IRtssIntegration rtss, IToolDetector tools, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public string RtssStatus { get; private set; } = "";
    public string AfterburnerStatus { get; private set; } = "";
    public string ScsStatus { get; private set; } = "";
    public string Message { get; private set; } = "";
    public bool RtssInstalled { get; private set; }
    public bool CanRestoreRtss { get; private set; }
    public bool CanRestoreLayout { get; private set; }
    public string LayoutStatus { get; private set; } = "";
    public string? ExportedPath { get; private set; }
    public bool Busy { get; private set; }

    public void Refresh()
    {
        try
        {
            var r = rtss.State(); RtssInstalled = r.Installed;
            RtssStatus = !r.Installed ? Text["IntNotInstalled"]
                : rtss.OverlayConfigured() switch { true => Text.Format("IntRtssLotus", r.Version ?? "?"), false => Text.Format("IntRtssDefault", r.Version ?? "?"), _ => Text.Format("IntRtssUnreadable", r.Version ?? "?") };
            CanRestoreRtss = r.Installed && rtss.HasBackup();
            CanRestoreLayout = r.Installed && rtss.HasLayoutBackup();
            LayoutStatus = !r.Installed ? "" : Text[rtss.LayoutActive() ? "IntLayoutActive" : "IntLayoutInactive"];
            var ab = tools.Afterburner(); AfterburnerStatus = ab.Installed ? Text.Format("IntAfterburnerOn", ab.Version ?? "?") : Text["IntNotInstalled"];
            var scs = tools.ScsKiller(); ScsStatus = scs.Installed ? Text.Format("IntScsOn", scs.Version ?? "?") : Text["IntScsOff"];
        }
        catch (Exception ex) { log(ex); }
        foreach (var p in new[] { nameof(RtssStatus), nameof(AfterburnerStatus), nameof(ScsStatus), nameof(RtssInstalled), nameof(CanRestoreRtss), nameof(CanRestoreLayout), nameof(LayoutStatus) }) OnPropertyChanged(p);
    }

    public Task ApplyOverlayAsync() => Run(async () => Message = Text[await rtss.ApplyLotusOverlayAsync() ? "IntOverlayApplied" : "IntOverlayFailed"]);
    public Task RestoreOverlayAsync() => Run(async () => Message = Text[await rtss.RestoreAsync() ? "IntOverlayRestored" : "IntOverlayFailed"]);
    public Task ActivateLayoutAsync() => Run(async () => Message = Text[await rtss.ActivateLayoutAsync() ? "IntLayoutApplied" : "IntOverlayFailed"]);
    public Task RestoreLayoutAsync() => Run(async () => Message = Text[await rtss.RestoreLayoutAsync() ? "IntLayoutRestored" : "IntOverlayFailed"]);
    // null when the file could not be written (locked, disk full); the message explains it.
    public string? ExportLayout()
    {
        try { ExportedPath = rtss.ExportLayout(); Message = Text.Format("IntLayoutExported", ExportedPath); }
        catch (Exception ex) { log(ex); ExportedPath = null; Message = Text["IntOverlayFailed"]; }
        OnPropertyChanged(nameof(Message));
        return ExportedPath;
    }

    private async Task Run(Func<Task> work)
    {
        if (Busy) return;
        Busy = true; OnPropertyChanged(nameof(Busy));
        try { await work(); } catch (Exception ex) { log(ex); Message = Text["IntOverlayFailed"]; }
        finally { Busy = false; OnPropertyChanged(nameof(Busy)); OnPropertyChanged(nameof(Message)); Refresh(); }
    }
    public void RefreshLanguage() => Refresh();
}
