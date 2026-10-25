using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Tools;

// "Desinstalar programas": the program's own registered uninstaller does the removal; afterwards Lotus looks for
// leftovers only in well-known places with the program's exact name, and moves what the player checks to the Recycle Bin.
public sealed record InstalledApp(string Key, string Name, string? Publisher, string? Version, long? SizeBytes, DateTime? Installed,
    string? InstallLocation, string UninstallCommand, bool UserScope, string? IconSource = null);
public sealed record Leftover(string Path, long Bytes, bool Recommended, bool NeedsAdmin);
public interface IAppUninstaller
{
    IReadOnlyList<InstalledApp> List();
    Task<int?> RunUninstallerAsync(InstalledApp app, CancellationToken token); // exit code; null when it could not start
    Task<bool> WaitUntilRemovedAsync(InstalledApp app, TimeSpan timeout, CancellationToken token);
    IReadOnlyList<Leftover> FindLeftovers(InstalledApp app, IReadOnlyList<InstalledApp> stillInstalled);
    int Recycle(IReadOnlyList<Leftover> items, IProgress<double>? progress = null); // returns how many left their folder
    string? IconFor(InstalledApp app); // cached PNG of the program's icon, or null
}

public sealed partial class LeftoverRow(Leftover item, string size) : ObservableObject
{
    public Leftover Item { get; } = item;
    public string Path => Item.Path;
    public string Size { get; } = size;
    public bool CanRecycle => !Item.NeedsAdmin;
    [ObservableProperty] private bool selected = item.Recommended && !item.NeedsAdmin;
}
public sealed partial class AppRowView(InstalledApp app, string detail) : ObservableObject
{
    public InstalledApp App { get; } = app;
    public string Detail { get; } = detail;
    public string Initial => App.Name.Length > 0 ? char.ToUpperInvariant(App.Name[0]).ToString() : "?";
    [ObservableProperty] private bool selected;
    [ObservableProperty] private string? iconPath;
}

public sealed class UninstallerViewModel(IAppUninstaller uninstaller, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    private List<InstalledApp> all = [];
    private readonly Dictionary<string, string?> icons = [];
    public int SelectedCount => Apps.Count(a => a.Selected);
    public bool HasSelection => SelectedCount > 0;
    public string UninstallSelectedLabel => Text.Format("UninSelected", SelectedCount);
    public void SelectionChanged() { OnPropertyChanged(nameof(SelectedCount)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(UninstallSelectedLabel)); }

    // Icons come from each program's own file, extracted in the background and cached by Lotus.
    private async System.Threading.Tasks.Task LoadIconsAsync()
    {
        foreach (var row in Apps.ToList())
        {
            if (icons.TryGetValue(row.App.Key, out var known)) { row.IconPath = known; continue; }
            string? path = null;
            try { path = await System.Threading.Tasks.Task.Run(() => uninstaller.IconFor(row.App)); } catch (Exception ex) { log(ex); }
            icons[row.App.Key] = path; row.IconPath = path;
        }
    }

    // Several at once: each program's own uninstaller runs in turn; the leftovers of all of them are reviewed together.
    public async System.Threading.Tasks.Task UninstallSelectedAsync()
    {
        var chosen = Apps.Where(a => a.Selected).Select(a => a.App).ToList();
        if (chosen.Count == 0 || Busy) return;
        Busy = true; Removed = null; Leftovers.Clear(); Job.Start(); Notify();
        var removed = new List<InstalledApp>();
        try
        {
            for (var i = 0; i < chosen.Count; i++)
            {
                var app = chosen[i];
                Status = Text.Format("UninBatchRunning", i + 1, chosen.Count, app.Name); Notify();
                if (await uninstaller.RunUninstallerAsync(app, CancellationToken.None) is not null
                    && await uninstaller.WaitUntilRemovedAsync(app, TimeSpan.FromSeconds(60), CancellationToken.None)) removed.Add(app);
                Job.Report((i + 1) * 85.0 / chosen.Count);
            }
            await LoadAsync();
            foreach (var app in removed)
                foreach (var l in await System.Threading.Tasks.Task.Run(() => uninstaller.FindLeftovers(app, all)))
                    if (Leftovers.All(x => x.Path != l.Path)) Leftovers.Add(new(l, l.Bytes >= 1L << 30 ? $"{l.Bytes / (double)(1L << 30):0.0} GB" : $"{Math.Max(1, l.Bytes / (1L << 20))} MB"));
            Status = Text.Format("UninBatchDone", removed.Count, chosen.Count) + (Leftovers.Count > 0 ? " " + Text.Format("UninBatchLeftovers", Leftovers.Count) : "");
            if (removed.Count > 0) Job.Finish(Text.Format("UninBatchDoneShort", removed.Count)); else Job.Fail();
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Job.Fail(); }
        finally { Busy = false; Notify(); SelectionChanged(); }
    }
    private string query = "";
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<AppRowView> Apps { get; } = [];
    public ObservableCollection<LeftoverRow> Leftovers { get; } = [];
    public TaskState Job { get; } = new();
    public string Status { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public InstalledApp? Removed { get; private set; }
    public bool HasLeftovers => Leftovers.Count > 0;
    public bool Busy { get; private set; }
    public string Query { get => query; set { if (SetProperty(ref query, value ?? "")) Filter(); } }
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);

    public async Task LoadAsync()
    {
        try { all = [.. (await System.Threading.Tasks.Task.Run(uninstaller.List)).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)]; }
        catch (Exception ex) { log(ex); all = []; }
        Summary = Text.Format("UninCount", all.Count); OnPropertyChanged(nameof(Summary));
        Filter();
    }

    private void Filter()
    {
        Apps.Clear();
        var terms = query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var a in all.Where(a => terms.All(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || (a.Publisher?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false))).Take(40))
        {
            var parts = new List<string>();
            if (a.Publisher is { Length: > 0 } p) parts.Add(p);
            if (a.Version is { Length: > 0 } v) parts.Add(v);
            if (a.SizeBytes is > 0 and var s) parts.Add(s >= 1L << 30 ? $"{s / (double)(1L << 30):0.0} GB" : $"{s / (double)(1L << 20):0} MB");
            if (a.Installed is { } d) parts.Add(d.ToString("d", Culture));
            Apps.Add(new(a, string.Join(" · ", parts)) { IconPath = icons.GetValueOrDefault(a.Key) });
        }
        SelectionChanged();
        _ = LoadIconsAsync();
    }

    // Runs the program's own uninstaller; when it is gone, looks for leftovers for the player to review.
    public async System.Threading.Tasks.Task UninstallAsync(InstalledApp app)
    {
        if (Busy) return;
        Busy = true; Removed = null; Leftovers.Clear(); Job.Start(); Status = Text.Format("UninRunning", app.Name); Notify();
        try
        {
            Job.Report(10);
            var code = await uninstaller.RunUninstallerAsync(app, CancellationToken.None);
            if (code is null) { Status = Text.Format("UninCouldNotStart", app.Name); Job.Fail(); return; }
            Job.Report(60);
            if (!await uninstaller.WaitUntilRemovedAsync(app, TimeSpan.FromSeconds(60), CancellationToken.None))
            { Status = Text.Format("UninStillThere", app.Name); Job.Fail(); return; }
            Job.Report(85);
            await LoadAsync();
            Removed = app;
            foreach (var l in await System.Threading.Tasks.Task.Run(() => uninstaller.FindLeftovers(app, all)))
                Leftovers.Add(new(l, l.Bytes >= 1L << 30 ? $"{l.Bytes / (double)(1L << 30):0.0} GB" : $"{Math.Max(1, l.Bytes / (1L << 20))} MB"));
            Status = Leftovers.Count == 0 ? Text.Format("UninCleanNoLeftovers", app.Name) : Text.Format("UninLeftoversFound", app.Name, Leftovers.Count);
            Job.Finish(Text.Format("UninDone", app.Name));
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Job.Fail(); }
        finally { Busy = false; Notify(); }
    }

    public async System.Threading.Tasks.Task RecycleLeftoversAsync()
    {
        var chosen = Leftovers.Where(l => l.Selected && l.CanRecycle).Select(l => l.Item).ToList();
        if (chosen.Count == 0 || Busy) return;
        Busy = true; Job.Start(); Notify();
        try
        {
            var progress = Job.Progress();
            var moved = await System.Threading.Tasks.Task.Run(() => uninstaller.Recycle(chosen, progress));
            foreach (var row in Leftovers.Where(l => l.Selected && l.CanRecycle).ToList()) Leftovers.Remove(row);
            Status = Text.Format("UninLeftoversRecycled", moved);
            Job.Finish(Text.Format("UninLeftoversDone", moved));
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Job.Fail(); }
        finally { Busy = false; Notify(); }
    }

    private void Notify() { foreach (var p in new[] { nameof(Status), nameof(Removed), nameof(HasLeftovers), nameof(Busy), nameof(Summary) }) OnPropertyChanged(p); }
}
