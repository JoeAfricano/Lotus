using Lotus.Core.Games;
using Lotus.Core.Score;
using Xunit;

namespace Lotus.Tests;

// Per-game settings: the same PC gets different settings for a light and a heavy game, from the game's FPS curve.
public sealed class GameAwareSettingsTests
{
    // José's PC: RTX 5060 8 GB, Ryzen 5 5500, 16 GB, 1080p at 74.973 Hz, no VRR (the Lotus Score calibration PC).
    private static readonly SystemProfile Pc = new("NVIDIA GeForce RTX 5060", 8192, 12, 16_000, 1920, 1080, 75, 74.973, "AMD Ryzen 5 5500");

    private static GameFpsCurve Curve(string name, Resolution r = Resolution.P1080, int single = 1400, int multi = 10500, int gpu = 11500)
    {
        var g = ScoreInsights.Games.First(x => x.Name == name);
        var l = Enum.GetValues<Preset>().Select(p => ScoreInsights.Limits(g, r, p, single, multi, gpu)).ToArray();
        return new([.. l.Select(x => x.Gpu)], [.. l.Select(x => x.Cpu)], g.Cap, "lab", g.Confidence);
    }

    [Fact] public void SamePcGetsDifferentSettingsPerGame()
    {
        var cs2 = SettingsAdvisor.Recommend(Pc, "balanced", false, Curve("Counter-Strike 2"));
        var wukong = SettingsAdvisor.Recommend(Pc, "balanced", false, Curve("Black Myth: Wukong"));
        Assert.Equal((QualityTier.Ultra, Upscaler.None), (cs2.Tier, cs2.Upscaler));
        Assert.Equal((QualityTier.High, Upscaler.Dlss, UpscaleMode.Quality), (wukong.Tier, wukong.Upscaler, wukong.Mode));
        Assert.All([cs2, wukong], r => { Assert.Equal(75, r.TargetFps); Assert.Contains("GsReasonGameAware", r.ReasonKeys); Assert.True(r.EstimatedFps >= 75 * 1.08 - 1); });
    }

    [Fact] public void GameCapBecomesTheTarget()
    {
        var elden = SettingsAdvisor.Recommend(Pc, "balanced", false, Curve("Elden Ring"));
        Assert.Equal(60, elden.TargetFps);
        Assert.Contains("GsReasonGameCap", elden.ReasonKeys);
        Assert.True(elden.EstimatedFps <= 60);
    }

    [Fact] public void ProcessorLimitIsCalledOutAndGraphicsStayUp()
    {
        // A 144 Hz monitor with a game whose processor ceiling (~110 FPS here) is below it.
        var fast = Pc with { RefreshHz = 144, ExactRefreshHz = 0 };
        var r = SettingsAdvisor.Recommend(fast, "balanced", false, Curve("Black Myth: Wukong", gpu: 30000));
        Assert.Contains("GsReasonCpuGame", r.ReasonKeys);
        Assert.True(r.TargetFps < 144);
    }

    [Fact] public void WeakerCardGetsLighterSettingsInTheSameGame()
    {
        var strong = SettingsAdvisor.Recommend(Pc, "balanced", false, Curve("Cyberpunk 2077"));
        var weak = SettingsAdvisor.Recommend(Pc with { GpuName = "NVIDIA GeForce GTX 1650", VramMb = 4096 }, "balanced", false, Curve("Cyberpunk 2077", gpu: 3500));
        Assert.True(weak.Tier < strong.Tier || weak.Mode > strong.Mode);
        Assert.NotEqual(Upscaler.Dlss, weak.Upscaler);
    }

    [Fact] public void HigherResolutionLowersTheChoice()
    {
        var p1080 = SettingsAdvisor.Recommend(Pc, "balanced", false, Curve("Cyberpunk 2077"));
        var p1440 = SettingsAdvisor.Recommend(Pc with { Width = 2560, Height = 1440 }, "balanced", false, Curve("Cyberpunk 2077", Resolution.P1440));
        Assert.True(p1440.Tier < p1080.Tier || p1440.Mode > p1080.Mode);
    }

    [Fact] public void UpscalerFollowsWhatTheGameShips()
    {
        var onlyFsr = new GameTech(Dlss: false, FrameGen: false, Fsr: true, Xess: false, Engine: null);
        // At 1440p the game's FSR is used (never DLSS it does not ship); at 1080p native Medium beats FSR's blur.
        var r = SettingsAdvisor.Recommend(Pc with { Width = 2560, Height = 1440 }, "balanced", false, Curve("Black Myth: Wukong", Resolution.P1440), onlyFsr);
        Assert.Equal(Upscaler.Fsr, r.Upscaler);
        var at1080 = SettingsAdvisor.Recommend(Pc, "balanced", false, Curve("Black Myth: Wukong"), onlyFsr);
        Assert.Equal((QualityTier.Medium, Upscaler.None), (at1080.Tier, at1080.Upscaler));
    }

    [Fact] public void NoCurveKeepsTheGenericRule()
    {
        var generic = SettingsAdvisor.Recommend(Pc);
        var none = SettingsAdvisor.Recommend(Pc, "balanced", false, null);
        var broken = SettingsAdvisor.Recommend(Pc, "balanced", false, new GameFpsCurve([0, 0, 0, 0], [1, 1, 1, 1], null, "lab"));
        Assert.Equal((generic.Tier, generic.Mode, generic.FpsCap), (none.Tier, none.Mode, none.FpsCap));
        Assert.Equal((generic.Tier, generic.Mode), (broken.Tier, broken.Mode));
        Assert.Equal(0, none.EstimatedFps);
        Assert.DoesNotContain("GsReasonGameAware", none.ReasonKeys);
    }

    [Fact] public void CompetitiveStillDropsQualityForLatency()
    {
        var balanced = SettingsAdvisor.Recommend(Pc, "balanced", false, Curve("Counter-Strike 2"));
        var competitive = SettingsAdvisor.Recommend(Pc, "competitive", false, Curve("Counter-Strike 2"));
        Assert.Equal(balanced.Tier - 2, competitive.Tier);
    }

    [Fact] public void HoldingTheRateLowersGraphicsAndSixtyIsTheOptionalChoice()
    {
        var pc = Pc with { Width = 2560, Height = 1440, ExactRefreshHz = 0 };
        var curve = Curve("Cyberpunk 2077", Resolution.P1440);
        var rate = SettingsAdvisor.Recommend(pc, "balanced", false, curve);
        Assert.Equal(75, rate.TargetFps);
        Assert.True(rate.CanPrefer60);
        Assert.False(rate.Prefer60);
        var sixty = SettingsAdvisor.Recommend(pc, "balanced", false, curve, prefer60: true);
        Assert.Equal((60, 60, true), (sixty.TargetFps, sixty.FpsCap, sixty.Prefer60));
        Assert.True(sixty.Tier > rate.Tier || sixty.Mode < rate.Mode);
        Assert.Contains("GsReasonPrefer60", sixty.ReasonKeys);
        // A game with frame generation: the generator is the better way, so 60 is not offered.
        var fg = new GameTech(Dlss: true, FrameGen: true, Fsr: false, Xess: false, Engine: null);
        Assert.False(SettingsAdvisor.Recommend(pc, "balanced", false, curve, fg, prefer60: true).Prefer60);
    }

    [Fact] public void SixtyIsOfferedWhenThePcFallsBelowTheMonitorRate()
    {
        // Reaches ~65 FPS even on Low at 75 Hz without FreeSync: by default Lotus locks half the rate (37);
        // the player may instead lock 60, which this PC holds.
        var curve = new GameFpsCurve([70, 58, 48, 38], [200, 200, 200, 200], null, "measured");
        var none = new GameTech(Dlss: false, FrameGen: false, Fsr: false, Xess: false, Engine: null);
        var half = SettingsAdvisor.Recommend(Pc, "balanced", false, curve, none);
        Assert.Equal(37, half.TargetFps);
        Assert.True(half.CanPrefer60);
        var sixty = SettingsAdvisor.Recommend(Pc, "balanced", false, curve, none, prefer60: true);
        Assert.Equal((60, 60, true), (sixty.TargetFps, sixty.FpsCap, sixty.Prefer60));
        Assert.Contains("GsReasonTargetLower", sixty.ReasonKeys);
        // Below 60 even on Low: nothing to offer.
        Assert.False(SettingsAdvisor.Recommend(Pc, "balanced", false, new GameFpsCurve([55, 45, 38, 30], [200, 200, 200, 200], null, "measured"), none).CanPrefer60);
        // Without data for the game (generic guess, e.g. Minecraft), never offered.
        Assert.False(SettingsAdvisor.Recommend(Pc, "balanced", false, null, none).CanPrefer60);
    }

    [Fact] public void NoUpscalerSaysWhyForEachGame()
    {
        var none = new GameTech(Dlss: false, FrameGen: false, Fsr: false, Xess: false, Engine: null);
        var dlss = new GameTech(Dlss: true, FrameGen: false, Fsr: false, Xess: false, Engine: null);
        var light = new GameFpsCurve([300, 260, 220, 180], [250, 250, 240, 230], null, "measured");
        Assert.Equal("GsUpscaleNoneInGame", SettingsAdvisor.NoUpscaleKey(SettingsAdvisor.Recommend(Pc, "balanced", false, null, none)));
        var holds = SettingsAdvisor.Recommend(Pc, "balanced", false, light, dlss);
        Assert.Equal(Upscaler.None, holds.Upscaler);
        Assert.Equal("GsUpscaleNotNeeded", SettingsAdvisor.NoUpscaleKey(holds));
        Assert.Equal("GsLineNoUpscale", SettingsAdvisor.NoUpscaleKey(SettingsAdvisor.Recommend(Pc with { Width = 1280, Height = 720 }, "balanced", false)));
    }

    [Fact] public void HeavyGameOnMidCardLowersGraphicsBeforeHalvingTheRate()
    {
        // GTX 1660 Super class in Cyberpunk at 1080p 75 Hz: Low/Medium reach 75, so no 37.5 lock.
        var r = SettingsAdvisor.Recommend(Pc with { GpuName = "NVIDIA GeForce GTX 1660 Super", VramMb = 6144 }, "balanced", false, Curve("Cyberpunk 2077", gpu: 6000));
        Assert.Equal(75, r.TargetFps);
        Assert.True(r.Tier < QualityTier.Ultra);
        Assert.NotEqual(Upscaler.Dlss, r.Upscaler);
    }

    [Fact] public void NoUpscalerSuggestedWhenTheScanFoundNone()
    {
        // Minecraft Java: scanned, no DLSS/FSR/XeSS files, not in the table → generic rule without an upscaler.
        var none = new GameTech(Dlss: false, FrameGen: false, Fsr: false, Xess: false, Engine: null, MainExe: "javaw.exe");
        var r = SettingsAdvisor.Recommend(Pc, "balanced", false, null, none);
        Assert.Equal((Upscaler.None, UpscaleMode.Off), (r.Upscaler, r.Mode));
        // Not scanned at all: the card's own upscaler, as before.
        Assert.Equal(Upscaler.Dlss, SettingsAdvisor.Recommend(Pc, "balanced", false, null, GameTech.Unknown).Upscaler);
    }

    [Theory]
    [InlineData("Memory: 16 GB RAM", 16)]
    [InlineData("Memória: 8 GB de RAM", 8)]
    [InlineData("Memory: 16384 MB RAM", 16)]
    [InlineData("Memory: 6.5 GB RAM", 7)]
    [InlineData("Memory: lots", null)]
    public void RecommendedRamIsRead(string line, int? gb) => Assert.Equal(gb, ScoreInsights.RamGb(line));

    [Fact] public void SteamBaseParsesAndRejectsBadRows()
    {
        var games = SteamDemandBase.Parse("""{"schema":1,"date":"2026-11-19","games":[{"appId":"1","name":"Ok","gpu":80,"cpu":90,"ramGb":16,"basis":"RTX 3060"},{"appId":"x","name":"Bad id","gpu":80,"cpu":90},{"appId":"2","name":"Bad gpu","gpu":0,"cpu":90}]}""");
        var g = Assert.Single(games);
        Assert.Equal(("Ok", 16, "baixa"), (g.Name, g.RamGb!.Value, g.Confidence));
        Assert.Empty(SteamDemandBase.Parse("""{"schema":2,"games":[]}"""));
    }

    [Fact] public void LessRamThanTheGameRecommendsIsCalledOut()
    {
        var curve = Curve("Counter-Strike 2") with { RamGb = 16 };
        Assert.Contains("GsReasonRamGame", SettingsAdvisor.Recommend(Pc with { RamMb = 8_000 }, "balanced", false, curve).ReasonKeys);
        Assert.DoesNotContain("GsReasonRamGame", SettingsAdvisor.Recommend(Pc with { RamMb = 16_300 }, "balanced", false, curve).ReasonKeys);
    }
}
