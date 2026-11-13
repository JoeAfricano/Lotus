using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class GpuTuningTests
{
    private static List<TuneSample> Load(double temp, params double[] clocks) => [.. clocks.Select(c => new TuneSample(temp, c, 140))];
    private static List<TuneSample> Steady(double temp, double clock, int n = 30) => [.. Enumerable.Repeat(new TuneSample(temp, clock, 140), n)];

    [Fact]
    public void Readiness_names_the_first_missing_piece()
    {
        Assert.Equal("afterburner", new TuneReadiness(true, false, false, false, false).Missing);
        Assert.Equal("rivatuner", new TuneReadiness(true, true, false, true, true).Missing);
        Assert.Null(new TuneReadiness(true, true, true, true, true).Missing);
        Assert.False(new TuneReadiness(false, true, true, true, true).CanStart);
    }

    [Fact]
    public void Undervolt_walks_voltage_down_while_stable()
    {
        var start = GpuTuning.Start(TuneGoal.Undervolt);
        Assert.Equal(950, start.VoltageMv); Assert.Equal(2700, start.CoreMhz);
        var r = GpuTuning.Judge(start, Steady(70, 2700), driverReset: false, lastStable: null);
        Assert.Equal(TuneVerdict.Stable, r.Verdict);
        Assert.Equal(925, r.Next!.VoltageMv);
        Assert.Equal(2700, r.Next.CoreMhz);
    }

    [Fact]
    public void Undervolt_crash_goes_back_to_the_last_stable_point()
    {
        var stable = GpuTuning.Start(TuneGoal.Undervolt) with { VoltageMv = 925 };
        var tried = stable with { VoltageMv = 900 };
        var r = GpuTuning.Judge(tried, Steady(70, 2700), driverReset: true, lastStable: stable);
        Assert.Equal(TuneVerdict.Crashed, r.Verdict);
        Assert.Equal(stable, r.Next);
    }

    [Fact]
    public void Overclock_keeps_seventy_percent_of_the_last_stable_offsets()
    {
        var best = new TuneStep(TuneGoal.Overclock, 250, 800, 0, 0);
        var r = GpuTuning.Judge(best with { CoreMhz = 300 }, Steady(72, 2900), driverReset: true, lastStable: best);
        Assert.Equal(TuneVerdict.Crashed, r.Verdict);
        Assert.Equal(175, r.Next!.CoreMhz);
        Assert.Equal(560, r.Next.MemoryMhz);
    }

    [Fact]
    public void Overclock_steps_up_while_stable()
    {
        var start = GpuTuning.Start(TuneGoal.Overclock);
        var r = GpuTuning.Judge(start, Steady(70, 2850), false, null);
        Assert.Equal(150, r.Next!.CoreMhz);
        Assert.Equal(300, r.Next.MemoryMhz);
    }

    [Fact]
    public void Too_hot_stops_the_test()
    {
        var r = GpuTuning.Judge(GpuTuning.Start(TuneGoal.Overclock), Steady(85, 2850), false, null);
        Assert.Equal(TuneVerdict.TooHot, r.Verdict);
        Assert.Null(r.Next); // nothing stable yet: back to stock
    }

    [Fact]
    public void A_clock_that_keeps_falling_is_throttling()
    {
        var clocks = Enumerable.Range(0, 30).Select(i => i % 2 == 0 ? 2800.0 : 2550.0).ToArray();
        var r = GpuTuning.Judge(GpuTuning.Start(TuneGoal.Undervolt), Load(70, clocks), false, null);
        Assert.Equal(TuneVerdict.Throttled, r.Verdict);
    }

    [Fact]
    public void Too_few_samples_is_no_verdict()
    {
        Assert.Equal(TuneVerdict.NoData, GpuTuning.Judge(GpuTuning.Start(TuneGoal.Undervolt), Steady(60, 2700, 3), false, null).Verdict);
    }
}
