using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Tools;

// "Ferramentas": small, safe helpers for a gaming PC. Read-only by default; every change asks first and is gentle
// (apps close normally, temp files go to the Recycle Bin).
public sealed record RunningApp(string Name, long Bytes, bool HasWindow, IReadOnlyList<int> Pids);
public interface IAppCloser { IReadOnlyList<RunningApp> List(); int CloseGracefully(RunningApp app); } // returns processes asked to close
public sealed record TempScan(int Items, long Bytes);
public interface ITempCleaner { TempScan Scan(); TempScan Recycle(IProgress<double>? progress = null); } // only items older than 24 h; Recycle returns what was moved
public sealed record PingStats(string Target, double? AverageMs, double? JitterMs, double LossPercent);
public interface IConnectionTester { Task<IReadOnlyList<PingStats>> RunAsync(CancellationToken token, IProgress<double>? progress = null); } // gateway first, then internet
public sealed record DiskInfo(string Name, string Media, string Health, long SizeBytes);
public sealed record VolumeInfo(string Letter, long FreeBytes, long TotalBytes);
public interface IDiskHealth { (IReadOnlyList<DiskInfo> Disks, IReadOnlyList<VolumeInfo> Volumes) Read(); }
public sealed record GpuDriver(string Name, string Version, DateTime? Date, string Vendor)
{
    // The number players know: NVIDIA shows 32.0.16.2101 as 621.01 (last five digits of the last two groups).
    public string FriendlyVersion
    {
        get
        {
            var p = Version.Split('.');
            if (Vendor != "NVIDIA" || p.Length != 4 || !p.All(x => x.Length > 0 && x.All(char.IsDigit))) return Version;
            var digits = p[2] + p[3].PadLeft(4, '0');
            return digits.Length < 5 ? Version : $"{digits[^5..^2]}.{digits[^2..]}";
        }
    }
}
public interface IDriverInfo { GpuDriver? Read(); }
public sealed record LatestDriver(string Name, string Version, DateTime? Date, string DetailsUrl);
public sealed record PendingDriver(string Title, string Manufacturer, DateTime? Date);
public sealed record DriverCheck(LatestDriver? GpuLatest, IReadOnlyList<PendingDriver> Others, string? GpuError, string? WindowsUpdateError);
public interface IDriverUpdates { Task<DriverCheck> CheckAsync(GpuDriver? gpu, CancellationToken token, IProgress<double>? progress = null); }
public sealed record DnsProvider(string Id, string Name, string Primary, string Secondary);
public sealed record DnsState(string Adapter, IReadOnlyList<string> Servers, bool Automatic, bool HasBackup);
public sealed record DnsResult(string Id, string Name, string Server, double? MedianMs, double FailPercent);
public interface IDnsService
{
    DnsState? Current();
    Task<IReadOnlyList<DnsResult>> BenchmarkAsync(CancellationToken token, IProgress<double>? progress = null);
    Task<bool> ApplyAsync(string providerId); // elevated helper; verified by reading the adapter back
    Task<bool> RestoreAsync();
}

public sealed partial class AppRow(RunningApp app, string sizeText) : ObservableObject
{
    public RunningApp App { get; } = app;
    public string Name => App.Name;
    public string SizeText { get; } = sizeText;
    public bool HasWindow => App.HasWindow;
    [ObservableProperty] private bool selected;
}
public sealed record InfoRow(string Title, string Detail, bool Warning);

public sealed class ToolsViewModel(IAppCloser apps, ITempCleaner temp, IConnectionTester net, IDiskHealth disks, IDriverInfo driver, ITextCatalog text, Action<Exception> log, IDnsService? dns = null, IDriverUpdates? updates = null, UninstallerViewModel? uninstall = null) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<AppRow> Apps { get; } = [];
    // "Overlays e travadas" (read-only check; set by the window).
    public Games.OverlayCheckViewModel? Overlays { get; set; }
    public ObservableCollection<InfoRow> NetRows { get; } = [];
    public ObservableCollection<InfoRow> DiskRows { get; } = [];
    public string MemoryStatus { get; private set; } = "";
    public string TempStatus { get; private set; } = "";
    public string NetStatus { get; private set; } = "";
    public string DriverStatus { get; private set; } = "";
    public string? DriverUrl { get; private set; }
    public bool HasTemp { get; private set; }
    public long TempBytes => tempScan?.Bytes ?? 0;
    public int TempItems => tempScan?.Items ?? 0;
    public string Message { get; private set; } = "";
    public string DiskTip { get; private set; } = "";
    public string NetTileStatus => NetStatus.Length > 0 ? NetStatus : Text["ToolNetTileHint"];
    public string DiskTileStatus => DiskTip.Length > 0 ? DiskTip : DiskRows.FirstOrDefault()?.Detail ?? "";
    // Progress and completion of each task, shown with a bar, percentage and a "done" check.
    public UninstallerViewModel? Uninstall { get; } = uninstall;
    public TaskState MemTask { get; } = new(); public TaskState TempTask { get; } = new();
    public TaskState NetTask { get; } = new(); public TaskState DnsTask { get; } = new();
    public bool Busy { get; private set; }
    private TempScan? tempScan;
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    public string Size(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Culture) + " GB" : (bytes / (double)(1L << 20)).ToString("0", Culture) + " MB";

    public async Task RefreshAsync()
    {
        await Run(async () =>
        {
            var list = await Task.Run(apps.List);
            Apps.Clear(); foreach (var a in list.Where(x => x.HasWindow).Take(8)) Apps.Add(new(a, Size(a.Bytes)));
            MemoryStatus = Apps.Count == 0 ? Text["ToolMemNone"] : Text["ToolMemPick"];
            tempScan = await Task.Run(temp.Scan); HasTemp = tempScan.Items > 0;
            TempStatus = HasTemp ? Text.Format("ToolTempFound", tempScan.Items, Size(tempScan.Bytes)) : Text["ToolTempNone"];
            var (d, v) = await Task.Run(disks.Read);
            DiskRows.Clear(); DiskTip = "";
            foreach (var x in d) DiskRows.Add(new(x.Name, Text.Format("ToolDiskLine", Text["ToolMedia" + x.Media], Text["ToolHealth" + x.Health], Size(x.SizeBytes)), x.Health != "Healthy"));
            foreach (var x in v)
            {
                var free = x.TotalBytes > 0 ? x.FreeBytes * 100.0 / x.TotalBytes : 100;
                DiskRows.Add(new(x.Letter, Text.Format("ToolVolumeLine", Size(x.FreeBytes), Size(x.TotalBytes), free), free < 15));
                if (free < 15) DiskTip = Text.Format("ToolDiskLow", x.Letter);
            }
            var g = await Task.Run(driver.Read);
            DriverStatus = g is null ? Text["ToolDriverNone"] : Text.Format("ToolDriverLine", g.Name, g.FriendlyVersion == g.Version ? g.Version : $"{g.FriendlyVersion} ({g.Version})", g.Date?.ToString("d", Culture) ?? "?");
            DriverUrl = g?.Vendor switch
            {
                "NVIDIA" => "https://www.nvidia.com/pt-br/drivers/",
                "AMD" => "https://www.amd.com/pt/support/download/drivers.html",
                "Intel" => "https://www.intel.com.br/content/www/br/pt/download-center/home.html",
                _ => null
            };
            RefreshDns();
        });
    }

    // Asks the selected apps to close normally (like clicking X): unsaved work prompts stay with the app.
    public async Task CloseSelectedAsync()
    {
        await Run(async () =>
        {
            var chosen = Apps.Where(a => a.Selected).ToList();
            MemTask.Start(); var asked = 0;
            for (var i = 0; i < chosen.Count; i++) { var app = chosen[i]; asked += await Task.Run(() => apps.CloseGracefully(app.App)); MemTask.Report((i + 1) * 70.0 / Math.Max(1, chosen.Count)); }
            for (var w = 1; w <= 3; w++) { await Task.Delay(500); MemTask.Report(70 + w * 10); } // give apps a moment to close
            var before = chosen.Sum(a => a.App.Bytes);
            var list = await Task.Run(apps.List);
            Apps.Clear(); foreach (var a in list.Where(x => x.HasWindow).Take(8)) Apps.Add(new(a, Size(a.Bytes)));
            var still = list.Where(a => chosen.Any(c => c.Name == a.Name)).Sum(a => a.Bytes);
            MemoryStatus = asked == 0 ? Text["ToolMemNothing"] : Text.Format("ToolMemFreed", Size(Math.Max(0, before - still)));
            if (asked == 0) MemTask.Fail(); else MemTask.Finish(Text.Format("ToolMemDone", Size(Math.Max(0, before - still))));
        });
    }

    public async Task RecycleTempAsync()
    {
        await Run(async () =>
        {
            TempTask.Start(); var progress = TempTask.Progress();
            var moved = await Task.Run(() => temp.Recycle(progress));
            tempScan = await Task.Run(temp.Scan); HasTemp = tempScan.Items > 0;
            TempStatus = Text.Format("ToolTempDone", moved.Items, Size(moved.Bytes)) + (HasTemp ? " " + Text["ToolTempInUse"] : "");
            TempTask.Finish(Text.Format("ToolTempDoneShort", moved.Items, Size(moved.Bytes)));
        });
    }

    public async Task TestConnectionAsync()
    {
        await Run(async () =>
        {
            NetStatus = Text["ToolNetRunning"]; Notify();
            NetTask.Start();
            var stats = await net.RunAsync(CancellationToken.None, NetTask.Progress());
            NetRows.Clear();
            foreach (var s in stats)
                NetRows.Add(new(s.Target == "gateway" ? Text["ToolNetGateway"] : s.Target,
                    s.AverageMs is null ? Text["ToolNetNoReply"] : Text.Format("ToolNetLine", s.AverageMs, s.JitterMs ?? 0, s.LossPercent), s.LossPercent > 0 || s.AverageMs is null or > 80));
            NetStatus = Verdict(stats);
            NetTask.Finish(Text["ToolTestDone"]);
        });
    }

    // ---- Outdated drivers: NVIDIA's service for the graphics card, Windows Update for the rest ----
    public TaskState DriverTask { get; } = new();
    public ObservableCollection<InfoRow> DriverRows { get; } = [];
    public string DriverUpdateStatus { get; private set; } = "";
    public string? GpuUpdateUrl { get; private set; }
    public bool HasWindowsDriverUpdates { get; private set; }
    public bool CanCheckDrivers => updates is not null;
    private GpuDriver? installedGpu;

    public async Task CheckDriversAsync()
    {
        if (updates is null) return;
        await Run(async () =>
        {
            DriverTask.Start();
            installedGpu ??= await Task.Run(driver.Read);
            var check = await updates.CheckAsync(installedGpu, CancellationToken.None, DriverTask.Progress());
            DriverRows.Clear(); GpuUpdateUrl = null;
            var gpuOutdated = false;
            if (check.GpuLatest is { } latest && installedGpu is { } g)
            {
                gpuOutdated = Newer(latest.Version, g.FriendlyVersion);
                GpuUpdateUrl = gpuOutdated ? latest.DetailsUrl : null;
                DriverRows.Add(new(g.Name, gpuOutdated
                    ? Text.Format("ToolDrvGpuOld", g.FriendlyVersion, latest.Version, latest.Date?.ToString("d", Culture) ?? "?")
                    : Text.Format("ToolDrvGpuOk", g.FriendlyVersion), gpuOutdated));
            }
            else if (installedGpu is { } other) DriverRows.Add(new(other.Name, Text[check.GpuError is null ? "ToolDrvGpuManual" : "ToolDrvGpuOffline"], false));
            foreach (var d in check.Others)
                DriverRows.Add(new(d.Title, Text.Format("ToolDrvPending", d.Manufacturer.Length > 0 ? d.Manufacturer : "?", d.Date?.ToString("d", Culture) ?? "?"), true));
            HasWindowsDriverUpdates = check.Others.Count > 0;
            var outdated = check.Others.Count + (gpuOutdated ? 1 : 0);
            DriverUpdateStatus = check.WindowsUpdateError is not null ? Text["ToolDrvWuError"] : outdated == 0 ? Text["ToolDrvAllOk"] : Text.Format("ToolDrvFound", outdated);
            DriverTask.Finish(outdated == 0 ? Text["ToolDrvDoneOk"] : Text.Format("ToolDrvDoneFound", outdated));
            foreach (var p in new[] { nameof(DriverUpdateStatus), nameof(GpuUpdateUrl), nameof(HasWindowsDriverUpdates) }) OnPropertyChanged(p);
        });
    }
    // "621.01" vs "617.42": compares the numeric parts; anything unparsable counts as not newer.
    public static bool Newer(string latest, string installed)
    {
        static int[]? Parts(string v) { var p = v.Split('.'); return p.All(x => int.TryParse(x, out _)) ? [.. p.Select(int.Parse)] : null; }
        if (Parts(latest) is not { } a || Parts(installed) is not { } b) return false;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0; var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    // ---- DNS: measured on this connection; switching asks first and can be undone ----
    public ObservableCollection<InfoRow> DnsRows { get; } = [];
    public string DnsStatus { get; private set; } = "";
    public string? RecommendedDns { get; private set; }
    public string RecommendedDnsLabel => RecommendedDns is { } id ? Text.Format("ToolDnsApply", DnsNames.GetValueOrDefault(id, id)) : "";
    public bool CanApplyDns => RecommendedDns is not null;
    public bool CanRestoreDns { get; private set; }
    public bool HasDns => dns is not null;
    private readonly Dictionary<string, string> DnsNames = [];

    public void RefreshDns()
    {
        if (dns?.Current() is not { } c) { DnsStatus = dns is null ? "" : Text["ToolDnsNoAdapter"]; NotifyDns(); return; }
        CanRestoreDns = c.HasBackup;
        DnsStatus = Text.Format(c.Automatic ? "ToolDnsCurrentAuto" : "ToolDnsCurrentManual", string.Join(", ", c.Servers), c.Adapter);
        NotifyDns();
    }

    public async Task BenchmarkDnsAsync()
    {
        if (dns is null) return;
        await Run(async () =>
        {
            DnsStatus = Text["ToolDnsRunning"]; NotifyDns();
            DnsTask.Start();
            var results = await dns.BenchmarkAsync(CancellationToken.None, DnsTask.Progress());
            DnsTask.Finish(Text["ToolTestDone"]);
            DnsRows.Clear(); DnsNames.Clear();
            foreach (var r in results.OrderBy(r => r.MedianMs ?? double.MaxValue))
            {
                DnsNames[r.Id] = r.Name;
                var title = r.Id == "current" ? Text.Format("ToolDnsCurrentRow", r.Server) : $"{r.Name} ({r.Server})";
                DnsRows.Add(new(title, r.MedianMs is null ? Text["ToolNetNoReply"] : Text.Format("ToolDnsLine", r.MedianMs, r.FailPercent), r.FailPercent > 0));
            }
            (RecommendedDns, var key) = Recommend(results);
            DnsStatus = Text.Format(key, RecommendedDns is { } id ? DnsNames[id] : "");
        });
        NotifyDns();
    }

    // Recommends a switch only when a public resolver is clearly faster (≥ 20% and ≥ 5 ms) and never failed.
    public static (string? Id, string Key) Recommend(IReadOnlyList<DnsResult> results)
    {
        var current = results.FirstOrDefault(r => r.Id == "current");
        var best = results.Where(r => r.Id != "current" && r.MedianMs is not null && r.FailPercent == 0).MinBy(r => r.MedianMs);
        if (best is null) return (null, "ToolDnsNoneWorks");
        if (current?.MedianMs is not { } now) return (best.Id, "ToolDnsSuggestBroken");
        return best.MedianMs < now * 0.8 && now - best.MedianMs >= 5 ? (best.Id, "ToolDnsSuggest") : (null, "ToolDnsKeep");
    }

    public async Task ApplyDnsAsync()
    {
        if (dns is null || RecommendedDns is not { } id) return;
        await Run(async () => { DnsStatus = Text[await dns.ApplyAsync(id) ? "ToolDnsApplied" : "ToolDnsFailed"]; RecommendedDns = null; });
        RefreshDnsKeepStatus();
    }
    public async Task RestoreDnsAsync()
    {
        if (dns is null) return;
        await Run(async () => DnsStatus = Text[await dns.RestoreAsync() ? "ToolDnsRestored" : "ToolDnsFailed"]);
        RefreshDnsKeepStatus();
    }
    private void RefreshDnsKeepStatus() { var keep = DnsStatus; RefreshDns(); DnsStatus = keep; NotifyDns(); }
    private void NotifyDns()
    {
        foreach (var p in new[] { nameof(DnsStatus), nameof(RecommendedDns), nameof(RecommendedDnsLabel), nameof(CanApplyDns), nameof(CanRestoreDns), nameof(HasDns) }) OnPropertyChanged(p);
    }

    // Plain-language verdict: local network vs internet.
    public string Verdict(IReadOnlyList<PingStats> stats)
    {
        var gateway = stats.FirstOrDefault(s => s.Target == "gateway");
        var internet = stats.Where(s => s.Target != "gateway").ToList();
        if (internet.All(s => s.AverageMs is null)) return Text["ToolNetOffline"];
        if (gateway is { AverageMs: > 10 } or { LossPercent: > 0 } or { JitterMs: > 5 }) return Text["ToolNetLocalBad"];
        var avg = internet.Where(s => s.AverageMs is not null).Average(s => s.AverageMs!.Value);
        var loss = internet.Max(s => s.LossPercent); var jitter = internet.Max(s => s.JitterMs ?? 0);
        return loss > 0 || jitter > 15 ? Text["ToolNetUnstable"] : avg > 80 ? Text["ToolNetSlow"] : Text["ToolNetGood"];
    }

    private async Task Run(Func<Task> work)
    {
        if (Busy) return;
        Busy = true; Message = ""; Notify();
        try { await work(); }
        catch (Exception ex) { log(ex); Message = Text["ToolFailed"]; foreach (var t in new[] { MemTask, TempTask, NetTask, DnsTask, DriverTask }) if (t.Running) t.Fail(); }
        finally { Busy = false; Notify(); }
    }
    private void Notify()
    {
        foreach (var p in new[] { nameof(MemoryStatus), nameof(TempStatus), nameof(NetStatus), nameof(DriverStatus), nameof(DriverUrl), nameof(HasTemp), nameof(Busy), nameof(Message), nameof(DiskTip), nameof(DiskTileStatus), nameof(NetTileStatus) }) OnPropertyChanged(p);
    }
    public void RefreshLanguage() => _ = RefreshAsync();
}
