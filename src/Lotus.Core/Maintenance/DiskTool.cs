using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// ---- Saúde do Disco: what Windows itself reports; anything it does not report is shown as unavailable ----

public sealed record DiskVolume(string Letter, long Free, long Total);
public sealed record DiskDetail(string Id, string Model, string Media, string Bus, long Size, string Health, string? Operational,
    int? TemperatureC, int? WearPercent, long? PowerOnHours, long? ReadErrors, long? WriteErrors, IReadOnlyList<DiskVolume> Volumes);

public interface IDiskInspector
{
    IReadOnlyList<DiskDetail> Read();
    // Windows' own online, read-only file system scan (chkdsk /scan) in a visible window; asks for administrator (UAC).
    bool StartScan(string letter);
}

public sealed record DiskFact(string Label, string Value, bool Warning);
public sealed record DiskScanTarget(string Letter, string Label);
public sealed partial class DiskToolRow(DiskDetail disk, string title, string summary, IReadOnlyList<DiskFact> facts, bool warning, Func<string, string>? scanLabel = null) : ObservableObject
{
    public DiskDetail Disk { get; } = disk;
    public string Title { get; } = title;
    public string Summary { get; } = summary;
    public IReadOnlyList<DiskFact> Facts { get; } = facts;
    public bool Warning { get; } = warning;
    public IReadOnlyList<string> Letters => [.. Disk.Volumes.Select(v => v.Letter)];
    // Plain-language button text ("Verificar C:") instead of the raw command.
    public IReadOnlyList<DiskScanTarget> ScanTargets => [.. Letters.Select(l => new DiskScanTarget(l, scanLabel?.Invoke(l) ?? l))];
}

public sealed partial class DiskToolViewModel(IDiskInspector inspector, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<DiskToolRow> Rows { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private bool busy;
    public bool CanAct => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    public string TileStatus => Summary.Length > 0 ? Summary : Text["DkTileHint"];
    partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(TileStatus));
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    public string Size(long bytes) => bytes >= 1L << 40 ? (bytes / (double)(1L << 40)).ToString("0.0", Culture) + " TB" : (bytes / (double)(1L << 30)).ToString("0", Culture) + " GB";

    // Health words are lowercase to read well inside a sentence; a fact value stands alone, like "Indisponível".
    private static string Capitalize(string v) => v.Length > 0 ? char.ToUpper(v[0]) + v[1..] : v;

    public static IReadOnlyList<DiskFact> Facts(DiskDetail d, ITextCatalog text, Func<long, string> size)
    {
        string Na(string? v) => v ?? text["DkUnavailable"];
        var facts = new List<DiskFact>
        {
            new(text["DkType"], text["ToolMedia" + d.Media] + (d.Bus.Length > 0 ? " · " + d.Bus : ""), false),
            new(text["DkCapacity"], size(d.Size), false),
            new(text["DkHealth"], Capitalize(text["ToolHealth" + d.Health]) + (d.Operational is { Length: > 0 } o && o != "OK" ? " · " + o : ""), d.Health is "Warning" or "Unhealthy"),
            new(text["DkTemp"], Na(d.TemperatureC is { } t ? $"{t} °C" : null), d.TemperatureC is > 60),
            new(text["DkWear"], Na(d.WearPercent is { } w ? text.Format("DkWearValue", w) : null), d.WearPercent is >= 80),
            new(text["DkHours"], Na(d.PowerOnHours is { } h ? text.Format("DkHoursValue", h) : null), false),
            new(text["DkErrors"], Na(d.ReadErrors is null && d.WriteErrors is null ? null : text.Format("DkErrorsValue", d.ReadErrors ?? 0, d.WriteErrors ?? 0)), (d.ReadErrors ?? 0) + (d.WriteErrors ?? 0) > 0),
        };
        foreach (var v in d.Volumes)
        {
            var free = v.Total > 0 ? v.Free * 100.0 / v.Total : 100;
            facts.Add(new(text.Format("DkVolume", v.Letter), text.Format("ToolVolumeLine", size(v.Free), size(v.Total), free), free < 10));
        }
        return facts;
    }

    public async Task LoadAsync()
    {
        if (Busy) return;
        Busy = true; Work.Start();
        try
        {
            var disks = await Task.Run(inspector.Read);
            Rows.Clear();
            foreach (var d in disks)
            {
                var facts = Facts(d, Text, Size);
                var warning = facts.Any(f => f.Warning);
                Rows.Add(new(d, d.Model, Text.Format("DkLine", Text["ToolMedia" + d.Media], Size(d.Size), Text["ToolHealth" + d.Health]), facts, warning, l => Text.Format("DkScanButton", l)));
            }
            var bad = Rows.Count(r => r.Warning);
            Summary = Rows.Count == 0 ? Text["DkNone"] : bad == 0 ? Text.Format(Rows.Count == 1 ? "DkAllOkOne" : "DkAllOk", Rows.Count) : Text.Format("DkAttention", bad);
            Status = Summary + (disks.All(d => d.TemperatureC is null && d.WearPercent is null) ? " " + Text["DkNeedsAdmin"] : "");
            Work.Finish(Text["DkRead"]);
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }

    public bool StartScan(string letter)
    {
        try
        {
            var ok = inspector.StartScan(letter);
            journal.Add(new(DateTimeOffset.Now, "disk", "scan", letter, ok));
            Status = Text.Format(ok ? "DkScanStarted" : "DkScanDeclined", letter);
            return ok;
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; return false; }
    }
}
