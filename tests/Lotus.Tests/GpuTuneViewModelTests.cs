using Lotus.Core;
using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class GpuTuneViewModelTests
{
    private static TuneReadiness Ready => new(true, true, true, true, true);

    [Fact] public async Task A_stable_undervolt_moves_one_step_and_keeps_the_last_stable()
    {
        var samples = Enumerable.Range(0, 20).Select(_ => new TuneSample(70, 2700, 120)).ToList();
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "NVIDIA GeForce RTX 5060", (_, _, _) => Task.FromResult<(IReadOnlyList<TuneSample>, bool)>((samples, false)));
        vm.Refresh();
        var first = vm.Current;
        await vm.TestAsync();
        Assert.Equal(TuneVerdict.Stable, vm.Result!.Verdict);
        Assert.Equal(first, vm.LastStable);
        Assert.Equal(first.VoltageMv - 25, vm.Current.VoltageMv); // next try: 25 mV lower at the same clock
        Assert.Contains("70", vm.Verdict);
    }

    [Fact] public async Task A_driver_reset_goes_back_and_stock_never_tests()
    {
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "NVIDIA GeForce RTX 5060", (_, _, _) => Task.FromResult<(IReadOnlyList<TuneSample>, bool)>(([], true)));
        vm.Refresh();
        await vm.TestAsync();
        Assert.Equal(TuneVerdict.Crashed, vm.Result!.Verdict);
        vm.Goal = TuneGoal.Stock;
        Assert.False(vm.CanTest);
        var notReady = new GpuTuneViewModel(new TextCatalog(), () => new(true, false, false, false, false), () => "RTX 5060", (_, _, _) => throw new InvalidOperationException());
        notReady.Refresh();
        Assert.False(notReady.CanTest); // no Afterburner: nothing to test with
    }

    [Fact] public void Overclock_ladder_stops_at_the_family_ceiling()
    {
        var card = GpuTuning.For("NVIDIA GeForce RTX 5060");
        Assert.Equal(450, card.OcCoreMax);
        Assert.Equal(375, GpuTuning.For("NVIDIA GeForce RTX 5060 Ti").OcCoreMax);
        var near = new TuneStep(TuneGoal.Overclock, 400, 900, 0, 0);
        Assert.Equal(450, GpuTuning.Next(near, TuneVerdict.Stable, near, card)!.CoreMhz);          // one more step allowed on a 5060
        var top = new TuneStep(TuneGoal.Overclock, 450, 1000, 0, 0);
        Assert.Equal(GpuTuning.Keep(top), GpuTuning.Next(top, TuneVerdict.Stable, top, card)); // ceiling: keep 70%
    }
}