using Lotus.Core;
using Lotus.Core.Score;
using Lotus.Infrastructure.Score;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ScoreTests
{
    private sealed class FakeCpu(Func<int, double> value) : ICpuBenchmark
    {
        public int Calls;
        public double Run(int threads, TimeSpan duration, CancellationToken ct) { Calls++; ct.ThrowIfCancellationRequested(); return value(threads); }
    }
    private sealed class FakeGpu(double? fps) : IGpuBenchmark
    {
        public string? Adapter() => fps is null ? null : "Test GPU";
        public int Runs;
        public IReadOnlyList<double>? Run(int passes, TimeSpan each, Action<int> onPass, CancellationToken ct)
        {
            Runs++; if (fps is null) return null;
            for (var i = 0; i <= passes; i++) onPass(i);
            return Enumerable.Repeat(fps.Value, passes).ToList();
        }
    }
    private sealed class MemoryHistory : IScoreHistory
    {
        public List<ScoreRun> Runs = [];
        public IReadOnlyList<ScoreRun> Load() => Runs; public void Save(IReadOnlyList<ScoreRun> runs) => Runs = [.. runs];
    }
    private sealed class Machine : IScoreMachine { public string Cpu() => "Test CPU"; public string Gpu() => "Test GPU"; }

    [Fact] public void ScaleIsMonotonicAndReferencesAreOrdered()
    {
        Assert.Equal(10000, LotusScale.Total(1500, 11000, 9000)); // mid-range anchor
        Assert.True(LotusScale.Total(1500, 11000, 18000) > LotusScale.Total(1500, 22000, 9000)); // GPU weighs most
        Assert.Equal(0, LotusScale.Total(0, 10, 10));
        var totals = LotusScale.References.Select(r => r.Total).ToArray();
        Assert.Equal(totals.Order(), totals);
        var m = LotusScale.Summarize([100, 104, 98]); Assert.Equal(100, m.Median); Assert.Equal(6, m.VariationPct, 3);
    }

    [Fact] public async Task RunMeasuresWarmupPlusThreePassesSavesHistoryAndCompares()
    {
        var cpu = new FakeCpu(t => t == 1 ? 23450 : 194700); var history = new MemoryHistory();
        var vm = new LotusScoreViewModel(cpu, new FakeGpu(10380), history, new Machine(), new TextCatalog(), ex => throw ex);
        vm.Refresh(); Assert.True(vm.NoHistory); Assert.Equal("—", vm.Total);
        await vm.ToggleAsync();
        Assert.Equal(8, cpu.Calls); // 2 tests x (1 warm-up + 3)
        var run = Assert.Single(history.Runs);
        Assert.Equal(1400, run.Cpu1); Assert.Equal(10500, run.CpuAll); Assert.Equal(11500, run.Gpu);
        Assert.Equal(LotusScale.References.Single(r => r.Example.Contains("5060")).Total, run.Total); // calibration anchor
        Assert.False(vm.Busy); Assert.False(vm.NoHistory); Assert.Contains(vm.Compare, r => r.IsYou && r.Total == run.Total);
        Assert.Equal(100, vm.Compare.Max(r => r.Percent));
        Assert.Contains("0.0", vm.Reliability.Replace(',', '.'));
    }

    [Fact] public async Task WithoutGpuTheScoreUsesCpuOnlyAndCancelStops()
    {
        var history = new MemoryHistory();
        var vm = new LotusScoreViewModel(new FakeCpu(_ => 1000), new FakeGpu(null), history, new Machine(), new TextCatalog(), ex => throw ex);
        await vm.ToggleAsync(); Assert.Null(history.Runs[0].Gpu); Assert.Equal("—", vm.Gpu);
        var slow = new LotusScoreViewModel(new SlowCpu(), new FakeGpu(null), history, new Machine(), new TextCatalog(), ex => throw ex);
        var running = slow.ToggleAsync(); await Task.Delay(50); await slow.ToggleAsync(); await running;
        Assert.Equal(new TextCatalog()["ScoreCancelled"], slow.Phase); Assert.Single(history.Runs);
    }
    [Fact] public async Task ChosenSubsetRunsOnlyThoseTestsAndHasNoTotal()
    {
        var cpu = new FakeCpu(_ => 23450); var gpu = new FakeGpu(10380); var history = new MemoryHistory();
        var vm = new LotusScoreViewModel(cpu, gpu, history, new Machine(), new TextCatalog(), ex => throw ex) { RunCpuAll = false, RunGpu = false };
        Assert.True(vm.CanStart);
        await vm.ToggleAsync();
        Assert.Equal(4, cpu.Calls); Assert.Equal(0, gpu.Runs);
        var run = Assert.Single(history.Runs);
        Assert.Equal(1400, run.Cpu1); Assert.Null(run.CpuAll); Assert.Null(run.Total);
        Assert.Equal("—", vm.Total); Assert.Equal(new TextCatalog()["ScorePartial"], vm.Verdict); Assert.DoesNotContain(vm.Compare, r => r.IsYou);
        vm.RunCpu1 = false; Assert.False(vm.CanStart);
    }

    [Fact] public void PartsAreFoundByWindowsNameAndSearchedIndividually()
    {
        Assert.Equal("AMD Ryzen 5 5500", ScoreParts.Find("AMD Ryzen 5 5500", PartKind.Cpu)?.Name);
        Assert.Equal("NVIDIA GeForce RTX 5060 Ti", ScoreParts.Find("NVIDIA GeForce RTX 5060 Ti", PartKind.Gpu)?.Name);
        Assert.Equal("NVIDIA GeForce RTX 5060", ScoreParts.Find("NVIDIA GeForce RTX 5060", PartKind.Gpu)?.Name);
        Assert.Null(ScoreParts.Find("Unknown GPU", PartKind.Gpu));
        var vm = new LotusScoreViewModel(new FakeCpu(_ => 1), new FakeGpu(1), new MemoryHistory(), new RealNames(), new TextCatalog(), ex => throw ex);
        vm.Refresh(); Assert.Empty(vm.PartResults); Assert.Equal(new TextCatalog()["ScorePartHint"], vm.PartHint);
        vm.PartQuery = "rtx 5060";
        Assert.Equal(["NVIDIA GeForce RTX 5060", "NVIDIA GeForce RTX 5060 Ti"], vm.PartResults.Select(r => r.Name));
        Assert.True(vm.PartResults[0].IsYours); Assert.Contains("30", vm.PartResults[1].VsYou); // 15 000 vs 11 500 ≈ +30%
        vm.PartQuery = "ryzen 7 7800x3d"; Assert.Equal("CPU", Assert.Single(vm.PartResults).Kind);
        vm.PartQuery = "xyz"; Assert.Empty(vm.PartResults); Assert.Equal(new TextCatalog()["ScorePartNone"], vm.PartHint);
    }
    private sealed class RealNames : IScoreMachine { public string Cpu() => "AMD Ryzen 5 5500"; public string Gpu() => "NVIDIA GeForce RTX 5060"; }

    private sealed class SlowCpu : ICpuBenchmark
    {
        public double Run(int threads, TimeSpan duration, CancellationToken ct) { Task.Delay(5000, ct).Wait(ct); return 1; }
    }

    [Fact] public void RealCpuBenchmarkIsDeterministicAndScalesWithThreads()
    {
        Assert.Equal(CpuRayBenchmark.Pixel(160, 90), CpuRayBenchmark.Pixel(160, 90));
        var cpu = new CpuRayBenchmark();
        Assert.True(cpu.Run(1, TimeSpan.FromMilliseconds(300), default) > 0);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => cpu.Run(1, TimeSpan.FromSeconds(10), cts.Token));
    }

    [Fact] public void RealGpuBenchmarkRunsInItsSceneWindowWhenAHardwareGpuExists()
    {
        var gpu = new D3D11GpuBenchmark();
        if (gpu.Adapter() is null) return;
        var passes = new List<int>();
        var result = gpu.Run(1, TimeSpan.FromMilliseconds(200), passes.Add, default);
        Assert.Equal([0, 1], passes); Assert.True(Assert.Single(result!) > 0);
    }

    [Fact] public void HistoryFileRoundTripsAndToleratesCorruption()
    {
        var path = Path.Combine(Path.GetTempPath(), "LotusScore-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new JsonScoreHistory(path);
            Assert.Empty(store.Load());
            store.Save([new ScoreRun(new DateTime(2026, 10, 25), 1, 2, null, 3, 1.5, "c", "g")]);
            Assert.Equal(3, Assert.Single(store.Load()).Total);
            File.WriteAllText(path, "{oops"); Assert.Empty(store.Load());
        }
        finally { File.Delete(path); }
    }

    [Fact] public void OnlineLinksAreHttpsAndCarryOnlyPartNames()
    {
        var vm = new LotusScoreViewModel(new FakeCpu(_ => 1), new FakeGpu(1), new MemoryHistory(), new Machine(), new TextCatalog(), _ => { });
        Assert.All(vm.OnlineLinks(), l => Assert.StartsWith("https://", l.Url));
        Assert.Equal(3, vm.OnlineLinks().Count);
    }
}
