using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Tools;

// "Ferramentas": small, safe helpers for a gaming PC. Read-only by default; every change asks first and is gentle
// (apps close normally, temp files go to the Recycle Bin).
public sealed record RunningApp(string Name, long Bytes, bool HasWindow, IReadOnlyList<int> Pids);
public interface IAppCloser { IReadOnlyList<RunningApp> List(); int CloseGracefully(RunningApp app); } // returns processes asked to close
public sealed record TempScan(int Items, long Bytes);
public interface ITempCleaner { TempScan Scan(); TempScan Recycle(); } // only items older than 24 h; Recycle returns what was moved
public sealed record PingStats(string Target, double? AverageMs, double? JitterMs, double LossPercent);
public interface IConnectionTester { Task<IReadOnlyList<PingStats>> RunAsync(CancellationToken token); } // gateway first, then internet
public sealed record DiskInfo(string Name, string Media, string Health, long SizeBytes);
public sealed record VolumeInfo(string Letter, long FreeBytes, long TotalBytes);
public interface IDiskHealth { (IReadOnlyList<DiskInfo> Disks, IReadOnlyList<VolumeInfo> Volumes) Read(); }
public sealed record GpuDriver(string Name, string Version, DateTime? Date, string Vendor);
public interface IDriverInfo { GpuDriver? Read(); }

public sealed partial class AppRow(RunningApp app, string sizeText) : ObservableObject
{
    public RunningApp App { get; } = app;
    public string Name => App.Name;
    public string SizeText { get; } = sizeText;
    public bool HasWindow => App.HasWindow;
    [ObservableProperty] private bool selected;
}
public sealed record InfoRow(string Title, string Detail, bool Warning);

public sealed class ToolsViewModel(IAppCloser apps, ITempCleaner temp, IConnectionTester net, IDiskHealth disks, IDriverInfo driver, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<AppRow> Apps { get; } = [];
    public ObservableCollection<InfoRow> NetRows { get; } = [];
    public ObservableCollection<InfoRow> DiskRows { get; } = [];
    public string MemoryStatus { get; private set; } = "";
    public string TempStatus { get; private set; } = "";
    public string NetStatus { get; private set; } = "";
    public string DriverStatus { get; private set; } = "";
    public string? DriverUrl { get; private set; }
    public bool HasTemp { get; private set; }
    public string Message { get; private set; } = "";
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
            DiskRows.Clear();
            foreach (var x in d) DiskRows.Add(new(x.Name, Text.Format("ToolDiskLine", Text["ToolMedia" + x.Media], Text["ToolHealth" + x.Health], Size(x.SizeBytes)), x.Health != "Healthy"));
            foreach (var x in v)
            {
                var free = x.TotalBytes > 0 ? x.FreeBytes * 100.0 / x.TotalBytes : 100;
                DiskRows.Add(new(x.Letter, Text.Format("ToolVolumeLine", Size(x.FreeBytes), Size(x.TotalBytes), free), free < 10));
            }
            var g = await Task.Run(driver.Read);
            DriverStatus = g is null ? Text["ToolDriverNone"] : Text.Format("ToolDriverLine", g.Name, g.Version, g.Date?.ToString("d", Culture) ?? "?");
            DriverUrl = g?.Vendor switch
            {
                "NVIDIA" => "https://www.nvidia.com/pt-br/drivers/",
                "AMD" => "https://www.amd.com/pt/support/download/drivers.html",
                "Intel" => "https://www.intel.com.br/content/www/br/pt/download-center/home.html",
                _ => null
            };
        });
    }

    // Asks the selected apps to close normally (like clicking X): unsaved work prompts stay with the app.
    public async Task CloseSelectedAsync()
    {
        await Run(async () =>
        {
            var chosen = Apps.Where(a => a.Selected).ToList();
            var asked = await Task.Run(() => chosen.Sum(a => apps.CloseGracefully(a.App)));
            await Task.Delay(1500);
            var before = chosen.Sum(a => a.App.Bytes);
            var list = await Task.Run(apps.List);
            Apps.Clear(); foreach (var a in list.Where(x => x.HasWindow).Take(8)) Apps.Add(new(a, Size(a.Bytes)));
            var still = list.Where(a => chosen.Any(c => c.Name == a.Name)).Sum(a => a.Bytes);
            MemoryStatus = asked == 0 ? Text["ToolMemNothing"] : Text.Format("ToolMemFreed", Size(Math.Max(0, before - still)));
        });
    }

    public async Task RecycleTempAsync()
    {
        await Run(async () =>
        {
            var moved = await Task.Run(temp.Recycle);
            tempScan = await Task.Run(temp.Scan); HasTemp = tempScan.Items > 0;
            TempStatus = Text.Format("ToolTempDone", moved.Items, Size(moved.Bytes)) + (HasTemp ? " " + Text["ToolTempInUse"] : "");
        });
    }

    public async Task TestConnectionAsync()
    {
        await Run(async () =>
        {
            NetStatus = Text["ToolNetRunning"]; Notify();
            var stats = await net.RunAsync(CancellationToken.None);
            NetRows.Clear();
            foreach (var s in stats)
                NetRows.Add(new(s.Target == "gateway" ? Text["ToolNetGateway"] : s.Target,
                    s.AverageMs is null ? Text["ToolNetNoReply"] : Text.Format("ToolNetLine", s.AverageMs, s.JitterMs ?? 0, s.LossPercent), s.LossPercent > 0 || s.AverageMs is null or > 80));
            NetStatus = Verdict(stats);
        });
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
        catch (Exception ex) { log(ex); Message = Text["ToolFailed"]; }
        finally { Busy = false; Notify(); }
    }
    private void Notify()
    {
        foreach (var p in new[] { nameof(MemoryStatus), nameof(TempStatus), nameof(NetStatus), nameof(DriverStatus), nameof(DriverUrl), nameof(HasTemp), nameof(Busy), nameof(Message) }) OnPropertyChanged(p);
    }
    public void RefreshLanguage() => _ = RefreshAsync();
}
