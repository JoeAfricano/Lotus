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
    private sealed class Temp : ITempCleaner { public int Recycled; public TempScan Scan() => Recycled > 0 ? new(0, 0) : new(5, 50L << 20); public TempScan Recycle(IProgress<double>? progress = null) { Recycled++; progress?.Report(100); return new(5, 50L << 20); } }
    private sealed class Net(params PingStats[] stats) : IConnectionTester { public Task<IReadOnlyList<PingStats>> RunAsync(CancellationToken t, IProgress<double>? progress = null) => Task.FromResult<IReadOnlyList<PingStats>>(stats); }
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
        Assert.True(vm.TempTask.Done); Assert.False(vm.TempTask.Running); Assert.Equal(100, vm.TempTask.Percent); Assert.True(vm.MemTask.Done);
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

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DnsTests
{
    private static DnsResult R(string id, double? ms, double fail = 0) => new(id, id, "1.2.3.4", ms, fail);

    [Fact] public void RecommendsOnlyAClearlyFasterWorkingResolver()
    {
        Assert.Equal(("cloudflare", "ToolDnsSuggest"), ToolsViewModel.Recommend([R("current", 60), R("cloudflare", 12), R("google", 30)]));
        Assert.Equal((null, "ToolDnsKeep"), ToolsViewModel.Recommend([R("current", 14), R("cloudflare", 12)]));       // not 20% better
        Assert.Equal(("google", "ToolDnsSuggest"), ToolsViewModel.Recommend([R("current", 60), R("cloudflare", 10, fail: 7), R("google", 20)])); // failures disqualify
        Assert.Equal(("quad9", "ToolDnsSuggestBroken"), ToolsViewModel.Recommend([R("current", null, 100), R("quad9", 15)]));
        Assert.Equal((null, "ToolDnsNoneWorks"), ToolsViewModel.Recommend([R("current", 20), R("google", null, 100)]));
    }

    [Fact] public void ElevatedHelperRejectsAnythingOutsideTheAllowList()
    {
        var data = Path.Combine(Path.GetTempPath(), "LotusDnsTest-" + Guid.NewGuid(), "Lotus"); Directory.CreateDirectory(data);
        try
        {
            Assert.Null(DnsElevatedHelper.TryRun(["--other"]));
            Assert.Equal(3, DnsElevatedHelper.TryRun(["--dns-set", "{00000000-0000-0000-0000-000000000000}", "evil-dns", "--lotus-data", data]));
            Assert.Equal(3, DnsElevatedHelper.TryRun(["--dns-set", "x' OR 1=1", "google", "--lotus-data", data])); // adapter id must be a GUID
            Assert.Equal(4, DnsElevatedHelper.TryRun(["--dns-restore", "--lotus-data", data]));                      // nothing to restore
            Assert.False(File.Exists(Path.Combine(data, "DnsBackup.json")));
        }
        finally { Directory.Delete(Path.GetDirectoryName(data)!, true); }
    }

    [Fact] public async Task RealQueryAnswersOrTimesOutQuietly()
    {
        var ms = await WindowsDnsService.QueryAsync("1.1.1.1", "www.google.com", default);
        Assert.True(ms is null or > 0);
        Assert.Contains(DnsProviders.All, p => p.Id == "cloudflare" && p.Primary == "1.1.1.1");
    }
}

public sealed class DriverVersionTests
{
    [Theory]
    [InlineData("NVIDIA", "32.0.16.2101", "621.01")]
    [InlineData("NVIDIA", "31.0.15.5222", "552.22")]
    [InlineData("NVIDIA", "32.0.15.6094", "560.94")]
    [InlineData("AMD", "32.0.12011.1036", "32.0.12011.1036")]
    [InlineData("NVIDIA", "garbage", "garbage")]
    public void ShowsTheNumberPlayersKnow(string vendor, string version, string expected) =>
        Assert.Equal(expected, new Lotus.Core.Tools.GpuDriver("GPU", version, null, vendor).FriendlyVersion);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DriverUpdateTests
{
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t)
        {
            var url = r.RequestUri!.ToString();
            var body = url.Contains("lookupValueSearch")
                ? "<?xml version=\"1.0\"?><LookupValueSearch><LookupValues><LookupValue ParentID=\"131\"><Name>NVIDIA GeForce RTX 5060 Ti</Name><Value>1076</Value></LookupValue><LookupValue ParentID=\"131\"><Name>NVIDIA GeForce RTX 5060</Name><Value>1078</Value></LookupValue></LookupValues></LookupValueSearch>"
                : url.Contains("psid=131&pfid=1078")
                    ? "{ \"Success\" : \"1\", \"IDS\" : [{ \"downloadInfo\": { \"Name\" : \"GeForce%20Game%20Ready%20Driver\", \"Version\" : \"625.10\", \"ReleaseDateTime\" : \"Wed Oct 21, 2026\", \"DetailsURL\" : \"https://www.nvidia.com/en-us/drivers/details/282412/\" } }] }"
                    : "{}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Theory]
    [InlineData("625.10", "621.01", true)] [InlineData("621.01", "621.01", false)] [InlineData("617.42", "621.01", false)]
    [InlineData("32.0.16.2101", "32.0.15.9999", true)] [InlineData("abc", "1.0", false)]
    public void ComparesVersionsNumerically(string latest, string installed, bool newer) => Assert.Equal(newer, Lotus.Core.Tools.ToolsViewModel.Newer(latest, installed));

    [Fact] public async Task ReadsLatestFromNvidiaServiceForTheExactCard()
    {
        var updates = new TrustedDriverUpdates(new HttpClient(new Handler()));
        var latest = await updates.NvidiaLatestAsync("NVIDIA GeForce RTX 5060", default);
        Assert.Equal(("GeForce Game Ready Driver", "625.10"), (latest!.Name, latest.Version));
        Assert.Equal("https://www.nvidia.com/en-us/drivers/details/282412/", latest.DetailsUrl);
        Assert.Null(await updates.NvidiaLatestAsync("NVIDIA GeForce RTX 9999", default));
    }
}
