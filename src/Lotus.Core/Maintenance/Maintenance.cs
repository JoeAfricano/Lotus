using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// Shared by every maintenance tool: how careful the player should be, and a short history of what was done.
public enum MaintenanceRisk { Low, Review, Advanced }

public sealed record MaintenanceEntry(DateTimeOffset At, string Tool, string Action, string Summary, bool Success);
public interface IMaintenanceJournal
{
    void Add(MaintenanceEntry entry);
    IReadOnlyList<MaintenanceEntry> Recent(int count = 20);
}
public sealed class MemoryMaintenanceJournal : IMaintenanceJournal
{
    private readonly List<MaintenanceEntry> items = [];
    public void Add(MaintenanceEntry entry) { lock (items) items.Add(entry); }
    public IReadOnlyList<MaintenanceEntry> Recent(int count = 20) { lock (items) return [.. items.AsEnumerable().Reverse().Take(count)]; }
}

// One line of the Lotus Clean list.
public sealed partial class CleanRow(CleanFinding finding, string title, string detail, string riskText) : ObservableObject
{
    public CleanFinding Finding { get; } = finding;
    public string Id => Finding.Rule.Id;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public string RiskText { get; } = riskText;
    public MaintenanceRisk Risk => Finding.Rule.Risk;
    public bool Blocked => Finding.Blocked;
    public bool CanSelect => !Blocked;
    [ObservableProperty] private bool selected;
}

// "Limpeza Inteligente": Analisar PC → review the list → Limpar selecionados → what really left the disk.
public sealed partial class CleanViewModel(CleanEngine engine, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<CleanRow> Rows { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool analyzed;
    private CancellationTokenSource? cts;
    public string TileStatus => Summary.Length > 0 ? Summary : Text["ClTileHint"];
    partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(TileStatus));

    public long SelectedBytes => Rows.Where(r => r.Selected && !r.Blocked).Sum(r => r.Finding.Bytes);
    public int SelectedCount => Rows.Count(r => r.Selected && !r.Blocked);
    public bool CanClean => !Busy && SelectedCount > 0;
    public string SelectedText => Text.Format("ClSelected", SelectedCount, Size(SelectedBytes));
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    public string Size(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Culture) + " GB"
        : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0", Culture) + " MB" : bytes >= 1024 ? (bytes / 1024.0).ToString("0", Culture) + " KB" : bytes > 0 ? "< 1 KB" : "0 KB";

    public async Task AnalyzeAsync()
    {
        if (Busy) return;
        Busy = true; cts = new(); Work.Start(); Status = Text["ClAnalyzing"];
        try
        {
            var report = await engine.AnalyzeAsync(Work.Progress(), cts.Token);
            foreach (var r in Rows) r.PropertyChanged -= OnRowChanged;
            Rows.Clear();
            foreach (var f in report.Findings.OrderBy(f => f.Rule.Category).ThenByDescending(f => f.Bytes))
            {
                var detail = f.Blocked ? Text.Format("ClBlocked", f.Files, Size(f.Bytes), f.BlockedBy!) : Text.Format("ClDetail", f.Files, Size(f.Bytes), Text["ClCat" + f.Rule.Category]);
                var row = new CleanRow(f, Text[f.Rule.TitleKey], detail, Text["Risk" + f.Rule.Risk]) { Selected = !f.Blocked && f.Rule.SelectedByDefault && f.Rule.Risk == MaintenanceRisk.Low };
                row.PropertyChanged += OnRowChanged;
                Rows.Add(row);
            }
            Analyzed = true;
            Summary = report.Bytes > 0 ? Text.Format("ClFound", Size(report.Bytes), report.Files) : Text["ClNothing"];
            Status = Summary;
            Work.Finish(Text["ClAnalyzed"]);
        }
        catch (OperationCanceledException) { Status = Text["ClCancelled"]; Work.Fail(); }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; cts?.Dispose(); cts = null; NotifySelection(); }
    }

    public async Task CleanAsync()
    {
        if (Busy || SelectedCount == 0) return;
        var ids = Rows.Where(r => r.Selected && !r.Blocked).Select(r => r.Id).ToList();
        Busy = true; cts = new(); Work.Start(); Status = Text["ClCleaning"];
        try
        {
            var result = await engine.CleanAsync(ids, Work.Progress(), cts.Token);
            var parts = new List<string> { Text.Format("ClFreed", Size(result.BytesFreed), result.Deleted) };
            if (result.InUse > 0) parts.Add(Text.Format("ClInUse", result.InUse));
            if (result.Failed > 0) parts.Add(Text.Format("ClFailedFiles", result.Failed));
            if (result.Skipped > 0) parts.Add(Text.Format("ClSkipped", result.Skipped));
            if (result.Cancelled) parts.Add(Text["ClCancelled"]);
            var text = string.Join(" ", parts);
            journal.Add(new(DateTimeOffset.Now, "clean", string.Join(",", ids), $"{result.Deleted} files, {result.BytesFreed} bytes, {result.InUse} in use, {result.Failed} failed", !result.Cancelled));
            Work.Finish(Text.Format("ClDoneShort", Size(result.BytesFreed)));
            // verify: analyse again so the list shows what is really left
            Busy = false;
            await AnalyzeAsync();
            Status = text;
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; NotifySelection(); }
    }

    public void Cancel() => cts?.Cancel();
    // "Recommended" = low risk and on by default (the Recycle Bin and other items to review are never ticked for you).
    public void SelectRecommended() { foreach (var r in Rows) r.Selected = !r.Blocked && r.Risk == MaintenanceRisk.Low && r.Finding.Rule.SelectedByDefault; }
    public void SelectNone() { foreach (var r in Rows) r.Selected = false; }
    public bool CanAnalyze => !Busy;
    public IReadOnlyList<MaintenanceEntry> History => journal.Recent(10);

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(CleanRow.Selected)) NotifySelection(); }
    private void NotifySelection() { foreach (var p in new[] { nameof(SelectedBytes), nameof(SelectedCount), nameof(CanClean), nameof(SelectedText) }) OnPropertyChanged(p); }
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanClean)); OnPropertyChanged(nameof(CanAnalyze)); }
}
