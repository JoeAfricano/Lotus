using Lotus.Core;
using Lotus.Core.Tools;
using Lotus.Infrastructure.Tools;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ToolsTests
{
    private sealed class Apps : IAppCloser
    {
        public List<RunningApp> Running = [new("Chrome", 3L << 30, true, [1]), new("Tray App", 1L << 30, false, [2])];
        public List<string> Closed = [];
        public IReadOnlyList<RunningApp> List() => Running;
        public int CloseGracefully(RunningApp app) { Closed.Add(app.Name); Running.RemoveAll(a => a.Name == app.Name); return 1; }
    }
    private sealed class Temp : ITempCleaner { public int Recycled; public TempScan Scan() => Recycled > 0 ? new(0, 0) : new(5, 50L << 20); public TempScan Recycle() { Recycled++; return new(5, 50L << 20); } }
    private sealed class Net(params PingStats[] stats) : IConnectionTester { public Task<IReadOnlyList<PingStats>> RunAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<PingStats>>(stats); }
    private sealed class Disks : IDiskHealth
    {
        public (IReadOnlyList<DiskInfo>, IReadOnlyList<VolumeInfo>) Read() => ([new("Samsung SSD", "Ssd", "Healthy", 1L << 40)], [new("C:", 5L << 30, 100L << 30)]);
    }
    private sealed class Driver : IDriverInfo { public GpuDriver? Read() => new("NVIDIA GeForce RTX 5060", "32.0.16.2101", new DateTime(2026, 9, 1), "NVIDIA"); }

    private static ToolsViewModel Vm(Apps apps, Temp temp, params PingStats[] net) =>
        new(apps, temp, new Net(net), new Disks(), new Driver(), new TextCatalog(), ex => throw ex);

    [Fact] public async Task ClosesOnlyCheckedAppsAndRecyclesTemp()
    {
        var apps = new Apps(); var temp = new Temp(); var vm = Vm(apps, temp);
        await vm.RefreshAsync();
        Assert.Single(vm.Apps); // the tray app without a window is not listed
        Assert.True(vm.HasTemp); Assert.Contains("50 MB", vm.TempStatus);
        Assert.Contains(vm.DiskRows, r => r.Title == "C:" && r.Warning); // 5% free
        Assert.Contains("32.0.16.2101", vm.DriverStatus); Assert.StartsWith("https://www.nvidia.com", vm.DriverUrl);
        await vm.CloseSelectedAsync(); Assert.Empty(apps.Closed); // nothing checked, nothing closed
        vm.Apps[0].Selected = true; await vm.CloseSelectedAsync();
        Assert.Equal(["Chrome"], apps.Closed); Assert.Contains("3", vm.MemoryStatus);
        await vm.RecycleTempAsync(); Assert.Equal(1, temp.Recycled); Assert.False(vm.HasTemp);
    }

    [Fact] public void ConnectionVerdictSeparatesHomeNetworkFromInternet()
    {
        var vm = Vm(new Apps(), new Temp()); var t = vm.Text;
        Assert.Equal(t["ToolNetGood"], vm.Verdict([new("gateway", 2, 1, 0), new("Cloudflare 1.1.1.1", 15, 2, 0)]));
        Assert.Equal(t["ToolNetLocalBad"], vm.Verdict([new("gateway", 35, 20, 5), new("Cloudflare 1.1.1.1", 60, 25, 5)]));
        Assert.Equal(t["ToolNetUnstable"], vm.Verdict([new("gateway", 2, 1, 0), new("Cloudflare 1.1.1.1", 30, 3, 10)]));
        Assert.Equal(t["ToolNetSlow"], vm.Verdict([new("gateway", 2, 1, 0), new("Google 8.8.8.8", 140, 4, 0)]));
        Assert.Equal(t["ToolNetOffline"], vm.Verdict([new("gateway", 2, 1, 0), new("Google 8.8.8.8", null, null, 100)]));
    }

    [Fact] public void TempCleanerSendsOnlyOldItemsToRecycleBin()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LotusToolsTest-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var old = Path.Combine(dir, "old.tmp"); var fresh = Path.Combine(dir, "fresh.tmp");
            File.WriteAllText(old, "x"); File.WriteAllText(fresh, "y");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
            var cleaner = new WindowsTempCleaner(dir);
            Assert.Equal(1, cleaner.Scan().Items);
            Assert.Equal(1, cleaner.Recycle().Items);
            Assert.False(File.Exists(old)); Assert.True(File.Exists(fresh));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact] public void RealReadersAreReadOnlyAndAnswer()
    {
        Assert.NotEmpty(new WindowsDiskHealth().Read().Volumes);
        _ = new WindowsDriverInfo().Read(); // may be null on machines without a display driver
        Assert.All(new WindowsAppCloser().List(), a => Assert.True(a.Bytes > 0));
    }
}
