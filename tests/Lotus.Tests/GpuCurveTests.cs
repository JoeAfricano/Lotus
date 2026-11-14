using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class GpuCurveTests
{
    // Part of the RTX 5060 curve read from the player's card (factory kHz per voltage point).
    private static readonly int[] Uv = [900_000, 925_000, 950_000, 1_000_000, 1_050_000, 1_065_000];
    private static readonly int[] Factory = [2_343_000, 2_426_000, 2_508_000, 2_688_000, 2_793_000, 2_962_000];

    [Fact] public void Flat_curve_reaches_the_target_and_never_goes_above_it()
    {
        var setting = new GpuSetting(0, 0, 950, 2700);
        var o = GpuCurve.Offsets(Uv, Factory, setting)!;
        Assert.Equal(2_700_000, Factory[2] + o[2]);                       // the 950 mV point lands on 2700 MHz
        for (var i = 0; i < o.Length; i++) Assert.True(Factory[i] + o[i] <= 2_700_000); // nothing above the target
        Assert.True(GpuCurve.Safe(Factory, o, setting, 450));
    }

    [Fact] public void Each_curve_depends_only_on_the_factory_curve_so_steps_never_add_up()
    {
        // The first run computed from (frequency − previous offset) and drifted upward step after step.
        var first = GpuCurve.Offsets(Uv, Factory, new GpuSetting(0, 0, 950, 2700))!;
        var second = GpuCurve.Offsets(Uv, Factory, new GpuSetting(0, 0, 925, 2700))!;
        Assert.Equal(2_700_000, Factory[1] + second[1]);
        Assert.Equal(GpuCurve.Offsets(Uv, Factory, new GpuSetting(0, 0, 925, 2700)), second); // same input, same curve
        Assert.True(second[0] > first[0]);                              // lower voltage point needs a bigger shift, by design
    }

    [Fact] public void Shifts_beyond_the_family_ceiling_are_refused()
    {
        var deep = new GpuSetting(0, 0, 900, 2900);                      // 900 mV would need +557 MHz
        var o = GpuCurve.Offsets(Uv, Factory, deep)!;
        Assert.False(GpuCurve.Safe(Factory, o, deep, 450));
        Assert.Null(GpuCurve.Offsets(Uv, Factory, new GpuSetting(0, 0, 1200, 2700))); // voltage not on the curve
    }
}
