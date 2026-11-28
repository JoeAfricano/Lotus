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

    [Fact] public void EveryFindingHasLocalizedTexts()
    {
        foreach (var language in new[] { "pt-BR", "en-US" })
        {
            var text = new TextCatalog(language);
            foreach (var key in new[] { "WhyShaders", "WhyMemory", "WhyProfile", "WhyGpuLimit", "WhyCpuLimit", "WhyUnknownStutter", "WhyAllGood" })
            { Assert.NotEmpty(text[key + "Title"]); Assert.NotEmpty(text.Format(key + "Body", "x")); }
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

// Wukong benchmark, 26/11/2026: limit 60 in the game on a 74.973 Hz screen, ~2,000 repeated frames in 2 min.
public sealed class GameCapBelowRefreshTests
{
    private static StutterInputs Inputs(double avg, double low, int spikes, double? gpu, int target) => new(avg, low, spikes, 30, gpu, target, ShaderState.Compiled, 0, 50, [], true);
    private static string[] Keys(StutterInputs i) => [.. StutterDiagnosis.Analyze(i).Select(f => f.Key)];
    private static StutterInputs Wukong(double? gpuMs, bool console = false, double? rtssCap = null, bool vrr = false, double low = 58) =>
        Inputs(60, low, 0, 60, 75) with { RealRefreshHz = 74.973, GpuFrameMs = gpuMs, ConsoleMode = console, FrameCap = rtssCap, AdaptiveSync = vrr };

    [Fact] public void GameLimitBelowTheScreenRateGetsTheMeasuredWayOut()
    {
        Assert.Equal("WhyGameCapUncap", Keys(Wukong(gpuMs: 10.4))[0]);      // the card has room for 75 Hz
        Assert.Equal("WhyGameCapConsole", Keys(Wukong(gpuMs: 14.0))[0]);    // it does not: Modo Console at 60 Hz
        Assert.Equal("WhyGameCapBoth", Keys(Wukong(gpuMs: null))[0]);       // card time unknown: both, no pick
        Assert.Equal("60 FPS / 74.973 Hz", StutterDiagnosis.Analyze(Wukong(10.4)).First().Detail);
        Assert.DoesNotContain("WhyCpuLimit", Keys(Wukong(10.4)));           // the game's own limit, not the processor
    }

    [Fact] public void NoGameLimitWarningWhereItDoesNotApply()
    {
        Assert.DoesNotContain(Keys(Wukong(10.4, console: true)), k => k.StartsWith("WhyGameCap"));     // Modo Console set
        Assert.DoesNotContain(Keys(Wukong(10.4, rtssCap: 59.99)), k => k.StartsWith("WhyGameCap"));   // RivaTuner's own plan
        Assert.DoesNotContain(Keys(Wukong(10.4, vrr: true)), k => k.StartsWith("WhyGameCap"));        // FreeSync/G-Sync
        Assert.DoesNotContain(Keys(Wukong(10.4, low: 35)), k => k.StartsWith("WhyGameCap"));          // not steady: hardware
        Assert.DoesNotContain(Keys(Inputs(74.9, 70, 0, 60, 75) with { RealRefreshHz = 74.973 }), k => k.StartsWith("WhyGameCap"));
        Assert.Equal(13.04, StutterDiagnosis.FastEnoughMs(74.973), 2);
    }
}