using Lotus.Core;
using Lotus.Core.Benchmark;
using Lotus.Core.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class HitchCausesTests
{
    // 10 s of 60 FPS frames with long frames placed at chosen seconds.
    private static List<FrameSample> Frames(params double[] hitchAt)
    {
        var list = new List<FrameSample>(); var t = 0.0;
        while (t < 10) { t += 1 / 60.0; list.Add(new(t, 16.7)); }
        foreach (var at in hitchAt) list.Add(new(at, 120));
        return [.. list.OrderBy(f => f.TimeSeconds)];
    }
    private static List<HitchSecond> Quiet() => [.. Enumerable.Range(0, 10).Select(_ => new HitchSecond(20, 2, 60))];

    [Fact] public void EachHitchGetsTheCauseOfItsSecond()
    {
        var s = Quiet();
        s[2] = s[2] with { PageInsPerSecond = 900 };  // page file read back: memory
        s[5] = s[5] with { GameReadMBps = 80 };       // the game reading files: disk
        s[7] = s[7] with { GpuPercent = 99 };         // GPU full
        var b = HitchCauses.Classify(Frames(2.5, 5.5, 7.5, 8.5, 9.5), s)!;
        Assert.Equal(new HitchBreakdown(5, 2, 1, 1, 1), b);
        Assert.Equal(HitchCause.Cpu, b.Main);
    }

    [Fact] public void BusyBaselineIsNotBlamedAndNothingWithoutData()
    {
        // Constant heavy paging in every second is the baseline, not what made one frame long.
        var s = Enumerable.Range(0, 10).Select(_ => new HitchSecond(400, 2, 60)).ToList();
        Assert.Equal(1, HitchCauses.Classify(Frames(4.5), s)!.Cpu);
        Assert.Null(HitchCauses.Classify(Frames(), s));           // no hitch
        Assert.Null(HitchCauses.Classify(Frames(4.5), []));        // no per-second data
        Assert.Null(HitchCauses.Classify([], s));
    }

    [Fact] public void DiagnosisNamesTheMainCauseOnlyWithAPattern()
    {
        StutterInputs At(HitchBreakdown? h, IReadOnlyList<string>? apps = null) => new(60, 30, 12, 30, 50, 60, null, 0, 70, apps ?? [], false, Hitches: h);
        var mem = StutterDiagnosis.Analyze(At(new(6, 1, 5, 0, 0), ["chrome"]));
        Assert.Contains(mem, f => f.Key == "WhyHitchMemory" && f.Fix == StutterFix.CloseApps);
        Assert.Contains(StutterDiagnosis.Analyze(At(new(6, 6, 0, 0, 0))), f => f.Key == "WhyHitchCpu" && f.Fix == StutterFix.None);
        Assert.DoesNotContain(StutterDiagnosis.Analyze(At(new(2, 2, 0, 0, 0))), f => f.Key.StartsWith("WhyHitch")); // too few
        var text = new TextCatalog();
        foreach (var c in new[] { "Cpu", "Memory", "Disk", "Gpu" })
            foreach (var part in new[] { "Title", "Body", "Tip" }) Assert.False(string.IsNullOrEmpty(text["WhyHitch" + c + part]));
    }
}
