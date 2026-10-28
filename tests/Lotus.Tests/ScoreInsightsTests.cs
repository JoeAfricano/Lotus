using Lotus.Core.Score;
using Xunit;

namespace Lotus.Tests;

public sealed class ScoreInsightsTests
{
    private const int S = 1400, M = 10500, G = 11500; // calibration PC (Ryzen 5 5500 + RTX 5060)
    private static GameDemand Game(string name) => ScoreInsights.Games.First(g => g.Name == name);

    [Fact] public void CalibrationPcMatchesTheTableAndScalesWithResolutionAndPreset()
    {
        var cp = Game("Cyberpunk 2077");
        var high = ScoreInsights.Estimate(cp, Resolution.P1080, Preset.High, S, M, G);
        Assert.InRange(high.Fps, 80, 86);                                  // GPU-limited, a bit under the 85 reference
        Assert.Equal(Limiter.Gpu, high.Limiter);
        var low = ScoreInsights.Estimate(cp, Resolution.P1080, Preset.Low, S, M, G).Fps;
        var k4 = ScoreInsights.Estimate(cp, Resolution.P2160, Preset.High, S, M, G).Fps;
        Assert.True(low > high.Fps && k4 < high.Fps);
        Assert.True(ScoreInsights.Estimate(cp, Resolution.P1080, Preset.High, S, M, G * 2).Fps > high.Fps); // faster GPU, more FPS
    }

    [Fact] public void CpuBoundGamesAndCapsAreReported()
    {
        var bg3 = ScoreInsights.Estimate(Game("Baldur's Gate 3"), Resolution.P1080, Preset.Low, S, M, G * 3);
        Assert.Equal(Limiter.Cpu, bg3.Limiter);                            // a huge GPU does not fix a CPU-heavy game
        var elden = ScoreInsights.Estimate(Game("Elden Ring"), Resolution.P1080, Preset.Low, S, M, G);
        Assert.Equal(60, elden.Fps); Assert.Equal(Limiter.Cap, elden.Limiter);
        Assert.Equal(0, ScoreInsights.Estimate(Game("Elden Ring"), Resolution.P1080, Preset.High, 0, M, G).Fps); // no reading, no number
    }

    [Fact] public void BottleneckMovesFromCpuToGpuAsResolutionGrows()
    {
        var weakCpuBigGpu = ScoreInsights.Bottlenecks(900, 5000, 36000);
        Assert.Equal(Limiter.Cpu, weakCpuBigGpu[0].Limiter);
        Assert.True(weakCpuBigGpu[0].Percent > weakCpuBigGpu[2].Percent);
        var strongCpuSmallGpu = ScoreInsights.Bottlenecks(2300, 40000, 3500);
        Assert.All(strongCpuSmallGpu, b => Assert.Equal(Limiter.Gpu, b.Limiter));
        Assert.Empty(ScoreInsights.Bottlenecks(0, 0, 0));
    }

    [Fact] public void SubScoresRankAndStability()
    {
        Assert.Equal(10000, ScoreInsights.CpuScore(1500, 11000));
        Assert.InRange(ScoreInsights.BeatsGpus(48000), 95, 100);
        Assert.InRange(ScoreInsights.BeatsGpus(2400), 0, 5);
        Assert.True(ScoreInsights.BeatsCpus(2300, 42000) > ScoreInsights.BeatsCpus(1200, 5400));
        Assert.Equal(98.5, ScoreInsights.Stability(1.5)); Assert.True(ScoreInsights.StabilityPasses(1.5)); Assert.False(ScoreInsights.StabilityPasses(4));
    }

    [Fact] public void PlayersGamesComeFirst()
    {
        var list = ScoreInsights.Ordered([("God of War", "1593500"), ("Unknown Indie", null), ("persona 5 royal", null)]);
        Assert.Equal(["God of War", "Persona 5 Royal"], list.Take(2).Select(g => g.Name));
        Assert.Equal(ScoreInsights.Games.Count, list.Count);
    }

    [Fact] public void AnyGameFromSteamRequirements()
    {
        var text = "SO: Windows 10\nProcessador: Intel Core i5-8400 / AMD Ryzen 5 2600\nPlaca de vídeo: NVIDIA GeForce GTX 1660 Super ou AMD Radeon RX 5500 XT\nArmazenamento: 50 GB";
        var g = ScoreInsights.FromRequirements("Some Game", "123", text);
        Assert.NotNull(g); Assert.Equal("123", g!.SteamAppId); Assert.Contains("1660", g.Basis);
        Assert.True(g.GpuFps > 120);                                          // a 5060 is much faster than a 1660 Super
        var onRec = ScoreInsights.Estimate(g, Resolution.P1080, Preset.High, S, M, G).Fps;
        Assert.True(onRec > 60);
        Assert.Null(ScoreInsights.FromRequirements("X", "1", "Placa de vídeo: Unknown Card 9000"));
        Assert.Null(ScoreInsights.FromRequirements("X", "1", null));
    }
}
