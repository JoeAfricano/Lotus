using Lotus.Core;
using Lotus.Core.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class StutterDiagnosisTests
{
    private static StutterInputs Inputs(double avg = 90, double low = 70, int spikes = 2, double? gpu = 80, int target = 75, ShaderState? shaders = ShaderState.Compiled,
        int pending = 0, double? memory = 50, bool canLower = true) => new(avg, low, spikes, 30, gpu, target, shaders, pending, memory, ["Chrome (3.1 GB)", "Discord (0.8 GB)"], canLower);
    private static string[] Keys(StutterInputs i) => [.. StutterDiagnosis.Analyze(i).Select(f => f.Key)];

    [Fact] public void StableGameIsAllGood() => Assert.Equal(["WhyAllGood"], Keys(Inputs()));

    [Fact] public void HitchesWithUncompiledShadersPointToShadersFirst()
    {
        var keys = Keys(Inputs(avg: 90, low: 40, shaders: ShaderState.Stale, pending: 2, memory: 90));
        Assert.Equal(["WhyShaders", "WhyMemory", "WhyProfile"], keys);
        var memory = StutterDiagnosis.Analyze(Inputs(low: 40, memory: 90)).Single(f => f.Key == "WhyMemory");
        Assert.Equal(StutterFix.CloseApps, memory.Fix); Assert.Contains("Chrome", memory.Detail);
    }

    [Fact] public void LowFpsSplitsGpuAndCpuLimits()
    {
        var gpu = StutterDiagnosis.Analyze(Inputs(avg: 50, low: 40, gpu: 98));
        Assert.Equal("WhyGpuLimit", gpu[0].Key); Assert.Equal(StutterFix.LowerQuality, gpu[0].Fix);
        Assert.Equal(StutterFix.None, StutterDiagnosis.Analyze(Inputs(avg: 50, low: 40, gpu: 98, canLower: false))[0].Fix);
        Assert.Equal(["WhyCpuLimit"], Keys(Inputs(avg: 50, low: 40, gpu: 45)));
    }

    [Fact] public void ManyHitchesWithoutKnownCauseAreReported() => Assert.Equal(["WhyUnknownStutter"], Keys(Inputs(spikes: 40)));

    // Stellar Blade on José's PC (21/11/2026): 67.6 FPS on a 74.973 Hz screen without VRR, 1,030 repeats in 5 minutes.
    private static StutterInputs Stellar(bool vrr = false, bool console = false, int? repeats = 1030, double avg = 67.6, double hz = 74.973, double? cap = 74.97) =>
        new(avg, 55, 0, 300, 55, 75, ShaderState.Compiled, 0, 70, [], true, null, null, cap, hz, vrr, repeats, console);
    [Fact] public void AGameBelowTheScreenRateGetsRepeatsAndConsoleMode()
    {
        var findings = StutterDiagnosis.Analyze(Stellar());
        Assert.Equal("WhyConsoleMode", findings[0].Key); Assert.Equal("68 FPS / 75 Hz", findings[0].Detail);
        Assert.Equal("206", findings.Single(f => f.Key == "WhyRepeats").Detail);
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(vrr: true)), f => f.Key == "WhyConsoleMode");     // the screen follows the game
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(console: true)), f => f.Key == "WhyConsoleMode"); // already on
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(avg: 74.5)), f => f.Key == "WhyConsoleMode");     // holds the rate
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(avg: 40)), f => f.Key == "WhyConsoleMode");       // too slow even for 60
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(hz: 60)), f => f.Key == "WhyConsoleMode");        // already a 60 Hz screen
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(avg: 58, cap: 58)), f => f.Key == "WhyConsoleMode"); // held by a limiter
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(repeats: 50)), f => f.Key == "WhyRepeats");          // 10 a minute: fine
        Assert.DoesNotContain(StutterDiagnosis.Analyze(Stellar(repeats: null)), f => f.Key == "WhyRepeats");        // not measured
    }

    [Fact] public void DisplayRepeatsCountFramesHeldForTwoRefreshes()
    {
        var intervals = Enumerable.Repeat(13.34, 200).Concat(Enumerable.Repeat(26.68, 10)).Concat([40.0]).ToList();
        Assert.Equal(11, Lotus.Core.Benchmark.DisplayRepeats.Count(intervals));
        Assert.Null(Lotus.Core.Benchmark.DisplayRepeats.Count([16.7, 33.4]));
    }

    [Fact] public void EveryFindingHasLocalizedTexts()
    {
        foreach (var language in new[] { "pt-BR", "en-US" })
        {
            var text = new TextCatalog(language);
            foreach (var key in new[] { "WhyShaders", "WhyMemory", "WhyProfile", "WhyGpuLimit", "WhyCpuLimit", "WhyUnknownStutter", "WhyAllGood", "WhyRepeats", "WhyConsoleMode" })
            { Assert.NotEmpty(text[key + "Title"]); Assert.NotEmpty(text.Format(key + "Body", "x")); Assert.NotEmpty(text[key + "Tip"]); }
        }
    }
}

public sealed class CpuHeatTests
{
    private static StutterInputs Run(CpuHeat? heat, double gpu = 60) => new(48, 20, 30, 30, gpu, 60, null, 0, 40, [], false, null, heat);

    [Fact] public void AHeldBackProcessorIsACauseWhenTheGameStutters()
    {
        var limited = Lotus.Core.Games.StutterDiagnosis.Analyze(Run(new(70, null)));
        Assert.Equal("WhyCpuHot", limited[0].Key);
        Assert.DoesNotContain(limited, f => f.Key == "WhyCpuLimit");                        // the same symptom, not twice
        var hot = Lotus.Core.Games.StutterDiagnosis.Analyze(Run(new(98, 93.4)));
        Assert.Equal(("WhyCpuHotTemp", "93"), (hot[0].Key, hot[0].Detail));
        Assert.DoesNotContain(Lotus.Core.Games.StutterDiagnosis.Analyze(Run(new(99, 60))), f => f.Key.StartsWith("WhyCpuHot"));
        Assert.DoesNotContain(Lotus.Core.Games.StutterDiagnosis.Analyze(new(120, 110, 0, 30, 60, 60, null, 0, 40, [], false, null, new(70, null))), f => f.Key.StartsWith("WhyCpuHot")); // smooth game: no worry
        Assert.Null(CpuHeat.From([new(null, null)]));
        Assert.Equal(new CpuHeat(80, 70), CpuHeat.From([new(90, 60), new(70, 70), new(null, null)]));
    }

    [Fact] public void TheSessionWarningNeedsMinutesNotASpike()
    {
        var watch = new CpuHeatWatch();
        for (var i = 0; i < CpuHeatWatch.LimitedSamples - 1; i++) Assert.False(watch.Feed(new(60, null)));
        Assert.False(watch.Feed(new(100, null)));                                             // back to normal: starts over
        for (var i = 0; i < CpuHeatWatch.LimitedSamples - 1; i++) Assert.False(watch.Feed(new(60, null)));
        Assert.True(watch.Feed(new(60, null)));
        Assert.False(watch.Feed(new(60, null)));                                              // once per session
        watch.Reset();
        for (var i = 0; i < CpuHeatWatch.HotSamples - 1; i++) Assert.False(watch.Feed(new(100, 96)));
        Assert.True(watch.Feed(new(100, 97))); Assert.Equal(97, watch.Last!.MaxZoneCelsius);
        Assert.False(new CpuHeatWatch().Feed(null));
    }
}
