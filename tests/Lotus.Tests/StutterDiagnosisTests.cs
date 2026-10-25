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
