using Lotus.Core.Games;
using Lotus.Infrastructure.Catalog;
using Lotus.Infrastructure.Games;
using Lotus.Infrastructure.Tools;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

// Optional checks against the real services and this PC (LOTUS_LIVE=1). Read-only: nothing is changed or installed.
public sealed class LiveServicesTests(ITestOutputHelper output)
{
    private static bool Live => Environment.GetEnvironmentVariable("LOTUS_LIVE") == "1" && OperatingSystem.IsWindows();

    [Fact] public async Task HowLongToBeatAnswers()
    {
        if (!Live) return;
        var hltb = new HowLongToBeatClient();
        var hades = await hltb.FindAsync("Hades", "1145360", CancellationToken.None);
        output.WriteLine($"Hades: {hades}");
        Assert.True(hades is { MainMinutes: > 600 });
        var byName = await hltb.FindAsync("Hollow Knight", null, CancellationToken.None);
        output.WriteLine($"Hollow Knight: {byName}");
        Assert.NotNull(byName);
    }

    [Fact] public async Task SaveManifestDownloadsAndFindsKnownGames()
    {
        if (!Live) return;
        var file = Path.Combine(Path.GetTempPath(), "lotus-live-manifest.json");
        var store = new SaveManifestStore(file);
        var manifest = await store.RefreshAsync(force: true);
        output.WriteLine($"games={manifest?.Games.Count} size={new FileInfo(file).Length / 1024} KB");
        Assert.NotNull(manifest);
        Assert.NotNull(store.Load()?.Find(new GameEntry(GameStores.Steam, "367520", "Hollow Knight")));
        Assert.Null(await store.RefreshAsync());                                                 // a week has not passed
        File.Delete(file);
    }

    [Fact] public async Task GpuDriverAndHeatAndBatteryReadOnThisPc()
    {
        if (!Live) return;
        var gpu = new WindowsDriverInfo().Read();
        output.WriteLine($"GPU: {gpu?.Name} {gpu?.Vendor} {gpu?.FriendlyVersion} {gpu?.Date:d}");
        if (gpu is { Vendor: "NVIDIA" })
        {
            var latest = await new TrustedDriverUpdates().NvidiaLatestAsync(gpu.Name, CancellationToken.None);
            output.WriteLine($"NVIDIA latest: {latest?.Version} {latest?.Date:d} {latest?.DetailsUrl}");
            output.WriteLine($"Notice: {Lotus.Core.Tools.DriverNotice.Decide(gpu, latest, null, DateTime.Today)}");
        }
        using var heat = new PdhCpuHeatSampler();
        var measured = await heat.MeasureAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
        output.WriteLine($"CPU heat: {measured}");
        output.WriteLine($"XInput batteries: {string.Join(", ", XInputBattery.Read())}");
    }
}
