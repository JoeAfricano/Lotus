using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class GameSettingsTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusGameSettings-" + Guid.NewGuid());

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5060", 8192, 145)]
    [InlineData("NVIDIA GeForce RTX 3060", 12288, 100)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", 16384, 196)]
    [InlineData("NVIDIA GeForce GTX 1650", 4096, 40)]
    [InlineData("AMD Radeon RX 6600", 8192, 95)]
    [InlineData("AMD Radeon RX 7800 XT", 16384, 237)]
    [InlineData("Intel(R) UHD Graphics 630", 128, 15)]
    [InlineData("Unknown GPU", 8192, 100)]
    public void GpuScoreHeuristic(string name, long vram, int expected) => Assert.Equal(expected, GpuScore.Estimate(name, vram));

    [Fact] public void RecommendationsScaleFromEntryToHighEnd()
    {
        var mid = SettingsAdvisor.Recommend(new("NVIDIA GeForce RTX 5060", 8192, 12, 16_000, 1920, 1080, 75));
        Assert.Equal((QualityTier.High, QualityTier.High, Upscaler.Dlss, UpscaleMode.Quality, 72), (mid.Tier, mid.TextureTier, mid.Upscaler, mid.Mode, mid.FpsCap));
        var top = SettingsAdvisor.Recommend(new("NVIDIA GeForce RTX 4090", 24576, 32, 64_000, 2560, 1440, 144));
        Assert.Equal((QualityTier.High, Upscaler.Dlss, UpscaleMode.Balanced, 144), (top.Tier, top.Upscaler, top.Mode, top.TargetFps));
        var entry = SettingsAdvisor.Recommend(new("NVIDIA GeForce GTX 1650", 4096, 4, 8_000, 1920, 1080, 144));
        Assert.Equal((QualityTier.Low, QualityTier.Low, Upscaler.Fsr, UpscaleMode.Balanced, 60), (entry.Tier, entry.TextureTier, entry.Upscaler, entry.Mode, entry.TargetFps));
        Assert.Contains("GsReasonTarget60", entry.ReasonKeys); Assert.Contains("GsReasonCpu", entry.ReasonKeys); Assert.Contains("GsReasonRam", entry.ReasonKeys);
        var igpu = SettingsAdvisor.Recommend(new("Intel(R) UHD Graphics 630", 0, 8, 16_000, 1920, 1080, 60));
        Assert.Equal(QualityTier.Low, igpu.Tier); Assert.Contains("GsReasonLowEnd", igpu.ReasonKeys);
        var amd = SettingsAdvisor.Recommend(new("AMD Radeon RX 7800 XT", 16384, 16, 32_000, 2560, 1440, 165));
        Assert.Equal((QualityTier.Ultra, Upscaler.Fsr, UpscaleMode.Quality, 60), (amd.Tier, amd.Upscaler, amd.Mode, amd.TargetFps));
    }

    private const string Ini = "[/Script/Engine.GameUserSettings]\r\nbUseVSync=False\r\nFrameRateLimit=0.000000\r\n\r\n[ScalabilityGroups]\r\nsg.ResolutionQuality=100\r\nsg.ShadowQuality=3\r\n\r\n[Custom]\r\nKeep=1";
    private (GameEntry Game, string Config, string LocalAppData) FakeUnrealGame(bool withConfig = true)
    {
        var install = Path.Combine(root, "Install");
        Directory.CreateDirectory(Path.Combine(install, "Engine"));
        var bin = Path.Combine(install, "MyGame", "Binaries", "Win64"); Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "MyGame-Win64-Shipping.exe"), "");
        var local = Path.Combine(root, "Local"); var config = Path.Combine(local, "MyGame", "Saved", "Config", "Windows", "GameUserSettings.ini");
        if (withConfig) { Directory.CreateDirectory(Path.GetDirectoryName(config)!); File.WriteAllText(config, Ini); }
        return (new GameEntry(GameStores.Steam, "1", "My Game", install), config, local);
    }

    [Fact] public void UnrealRecipeChangesOnlyKnownKeys()
    {
        var (game, config, local) = FakeUnrealGame();
        var recipe = new UnrealEngineRecipe(local);
        Assert.Equal("MyGame", UnrealEngineRecipe.ProjectName(game.InstallDir!));
        Assert.True(recipe.Probe(game)!.CanApply);
        var r = SettingsAdvisor.Recommend(new("NVIDIA GeForce RTX 5060", 8192, 12, 16_000, 1920, 1080, 75));
        var ini = new IniText(recipe.Transform(File.ReadAllText(config), r));
        Assert.Equal("2", ini.Get("ScalabilityGroups", "sg.ShadowQuality")); Assert.Equal("2", ini.Get("ScalabilityGroups", "sg.TextureQuality"));
        Assert.Equal("100", ini.Get("ScalabilityGroups", "sg.ResolutionQuality"));
        Assert.Equal("72.000000", ini.Get("/Script/Engine.GameUserSettings", "FrameRateLimit")); // 3 below the 75 Hz monitor
        Assert.Equal("False", ini.Get("/Script/Engine.GameUserSettings", "bUseVSync")); Assert.Equal("1", ini.Get("Custom", "Keep"));
        Assert.Null(recipe.Probe(new GameEntry(GameStores.Steam, "2", "Other", Path.Combine(root, "Nope"))));
    }

    [Fact] public void UnrealRecipeAsksToRunTheGameOnceWhenConfigIsMissing()
    {
        var (game, _, local) = FakeUnrealGame(withConfig: false);
        var support = new UnrealEngineRecipe(local).Probe(game)!;
        Assert.False(support.CanApply); Assert.Equal("GsRunOnce", support.Detail);
    }

    [Fact] public void IniTextAddsKeysAndSectionsWithoutDroppingLines()
    {
        var ini = new IniText("; comment\n[A]\nx=1\n");
        ini.Set("A", "y", "2"); ini.Set("A", "x", "3"); ini.Set("B", "z", "4");
        var text = ini.ToString();
        Assert.StartsWith("; comment", text); Assert.Equal("3", ini.Get("A", "x")); Assert.Equal("2", ini.Get("A", "y")); Assert.Equal("4", ini.Get("B", "z"));
    }

    private sealed class FixedProfile : ISystemProfileSource { public SystemProfile Read() => new("NVIDIA GeForce RTX 5060", 8192, 12, 16_000, 1920, 1080, 75); }
    private sealed class Running : IGameProcessCheck { public bool Value; public bool IsRunning(GameEntry game) => Value; }
    private sealed class NoLibrary : IGameLibrary { public LibraryScan Scan(IReadOnlyList<GameEntry> manual) => new([], []); }
    private sealed class NoStore : IManualGameStore { public IReadOnlyList<GameEntry> Load() => []; public void Save(IReadOnlyList<GameEntry> g) { } }
    private sealed class NoLauncher : IGameLauncher { public void Launch(GameEntry game) { } }

    [Fact] public async Task ApplyBacksUpOriginalBlocksRunningGameAndRestores()
    {
        var (game, config, local) = FakeUnrealGame(); var running = new Running();
        var vm = new GamesViewModel(new NoLibrary(), new NoStore(), new NoLauncher(), new TextCatalog(), _ => { },
            new FixedProfile(), [new UnrealEngineRecipe(local)], new FileGameSettingsStore(Path.Combine(root, "Backups")), running);
        var analysis = (await vm.AnalyzeSettingsAsync(game))!;
        Assert.True(analysis.Support!.CanApply); Assert.False(analysis.HasOriginal);
        Assert.Contains(analysis.Lines, l => l.Contains("DLSS")); Assert.Contains("RTX 5060", analysis.Hardware);
        running.Value = true;
        Assert.Equal("GsCloseGame", await vm.ApplySettingsAsync(game, analysis.Recommendation)); Assert.Equal(Ini, File.ReadAllText(config));
        running.Value = false;
        Assert.Equal("GsApplied", await vm.ApplySettingsAsync(game, analysis.Recommendation)); Assert.NotEqual(Ini, File.ReadAllText(config));
        Assert.Equal("GsAlready", await vm.ApplySettingsAsync(game, analysis.Recommendation));
        Assert.True((await vm.AnalyzeSettingsAsync(game))!.HasOriginal);
        Assert.Equal("GsRestored", await vm.RestoreSettingsAsync(game));
        Assert.Equal(Ini, File.ReadAllText(config)); Assert.False((await vm.AnalyzeSettingsAsync(game))!.HasOriginal);
        Assert.Equal("GsNoOriginal", await vm.RestoreSettingsAsync(game));
    }

    [Fact] public async Task WriteFailureBecomesStatusInsteadOfCrash()
    {
        var (game, config, local) = FakeUnrealGame(); var errors = 0;
        var vm = new GamesViewModel(new NoLibrary(), new NoStore(), new NoLauncher(), new TextCatalog(), _ => errors++,
            new FixedProfile(), [new UnrealEngineRecipe(local)], new FileGameSettingsStore(Path.Combine(root, "Backups")), new Running());
        var analysis = (await vm.AnalyzeSettingsAsync(game))!;
        using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.None)) // locked by another program
            Assert.Equal("GsWriteFailed", await vm.ApplySettingsAsync(game, analysis.Recommendation));
        Assert.Equal(1, errors); Assert.Equal(Ini, File.ReadAllText(config));
    }

    [Fact] public async Task GamesWithoutRecipeGetRecommendationOnly()
    {
        var vm = new GamesViewModel(new NoLibrary(), new NoStore(), new NoLauncher(), new TextCatalog(), _ => { }, new FixedProfile(), [new UnrealEngineRecipe(Path.Combine(root, "L"))]);
        var game = new GameEntry(GameStores.Steam, "1593500", "God of War", Path.Combine(root, "GoW"));
        var analysis = (await vm.AnalyzeSettingsAsync(game))!;
        Assert.Null(analysis.Support); Assert.NotEmpty(analysis.Lines);
        Assert.Equal("GsNotSupported", await vm.ApplySettingsAsync(game, analysis.Recommendation));
    }

    [Fact, System.Runtime.Versioning.SupportedOSPlatform("windows")] public void RealSystemProfileIsReadOnly()
    {
        var p = new WindowsSystemProfile(new Lotus.Infrastructure.Optimization.WindowsDisplayModes()).Read();
        var r = SettingsAdvisor.Recommend(p);
        output.WriteLine($"{p} => score {r.GpuScore}, {r.Tier}, textures {r.TextureTier}, {r.Upscaler} {r.Mode}, cap {r.FpsCap}");
        Assert.False(string.IsNullOrWhiteSpace(p.GpuName)); Assert.True(p.VramMb > 0);
    }

    [Theory]
    [InlineData(50, 40, 75, 98.0, -1, "GsTuneLower")]
    [InlineData(50, 40, 75, 55.0, 0, "GsTuneCpuBound")]
    [InlineData(75, 70, 75, 50.0, 1, "GsTuneRaise")]
    [InlineData(75, 70, 75, 90.0, 0, "GsTuneGood")]
    [InlineData(75, 40, 75, 99.0, -1, "GsTuneLower")]
    [InlineData(50, 40, 75, null, -1, "GsTuneLower")]
    public void TunerSeparatesGpuAndCpuBottlenecks(double avg, double low, int target, double? gpu, int step, string reason) =>
        Assert.Equal(new TuningDecision(step, reason), QualityTuner.Decide(avg, low, target, gpu, canRaise: true, canLower: true));

    [Fact] public void TunerRespectsLimits()
    {
        Assert.Equal("GsTuneAtMinimum", QualityTuner.Decide(40, 30, 60, 99, canRaise: true, canLower: false).ReasonKey);
        Assert.Equal("GsTuneGood", QualityTuner.Decide(60, 58, 60, 40, canRaise: false, canLower: true).ReasonKey);
    }

    private const string Nrftw = """{"Version":22,"VSyncEnabled":true,"FrameRateCap":0,"DisplayContrast":-2.384185791015625e-7,"QualityPreset":4,"UpscaleMethod":1,"UpscaleQualityMode":0,"OverallVolume":1.0}""";
    [Fact, System.Runtime.Versioning.SupportedOSPlatform("windows")] public void NoRestRecipeKeepsQualityAndOtherValues()
    {
        var r = SettingsAdvisor.Recommend(new("NVIDIA GeForce RTX 5060", 8192, 12, 16_000, 1920, 1080, 75));
        var json = NoRestForTheWickedRecipe.TransformJson(Nrftw, r);
        Assert.Contains("\"FrameRateCap\":72", json); Assert.Contains("\"QualityPreset\":4", json); // initial apply never lowers quality
        Assert.Contains("\"OverallVolume\":1.0", json); Assert.Contains("-2.384185791015625e-7", json);
        var recipe = new NoRestForTheWickedRecipe(@"Software\Lotus.Tests.Nonexistent");
        Assert.Contains("\"QualityPreset\":3", recipe.StepQuality(Nrftw, -1)); Assert.Null(recipe.StepQuality(Nrftw, +1));
        Assert.Null(recipe.Probe(new GameEntry(GameStores.Steam, "1", "Other", root)));
    }

    [Fact] public void UnrealStepMovesAllGroupsButTextures()
    {
        var recipe = new UnrealEngineRecipe(root);
        var lowered = new IniText(recipe.StepQuality(Ini, -1)!);
        Assert.Equal("2", lowered.Get("ScalabilityGroups", "sg.ShadowQuality")); Assert.Null(lowered.Get("ScalabilityGroups", "sg.TextureQuality"));
        var allLow = "[ScalabilityGroups]\r\n" + string.Join("\r\n", new[] { "View", "AntiAliasing", "Shadow", "GlobalIllumination", "Reflection", "PostProcess", "Effects", "Foliage", "Shading" }.Select(g => $"sg.{g}Quality=0"));
        Assert.Null(recipe.StepQuality(allLow.Replace("sg.ViewQuality", "sg.ViewDistanceQuality"), -1));
    }

    private sealed class FakeCapture(double avg, double low) : Lotus.Core.Benchmark.IFrameCaptureProvider
    {
        public Task<Lotus.Core.Benchmark.BenchmarkSession> CaptureAsync(Lotus.Core.Benchmark.CaptureRequest request, CancellationToken stop) =>
            Task.FromResult(new Lotus.Core.Benchmark.BenchmarkSession(Guid.NewGuid(), request.Target, DateTimeOffset.UtcNow, request.Seconds, "Completed", "test", "test", "", "", "", "{}",
                new("sc", 0, 0, 100, [], new(100, request.Seconds, avg, low, 20, 25, 16, 0, 50), "", false), ""));
    }
    private sealed class FakeSampler(double? load) : IGpuLoadSampler { public Task<double?> AverageAsync(TimeSpan d, CancellationToken t) => Task.FromResult(load); }
    private sealed class Found : IGameProcessCheck { public bool Run = true; public bool IsRunning(GameEntry g) => Run; public RunningGame? Find(GameEntry g) => Run ? new(1234, "MyGame-Win64-Shipping.exe", DateTimeOffset.UtcNow) : null; }

    [Fact] public async Task MeasureThenApplyLowersOneLevelOnlyAfterTheGameCloses()
    {
        var (game, config, local) = FakeUnrealGame(); var proc = new Found();
        var vm = new GamesViewModel(new NoLibrary(), new NoStore(), new NoLauncher(), new TextCatalog(), _ => { }, new FixedProfile(), [new UnrealEngineRecipe(local)],
            new FileGameSettingsStore(Path.Combine(root, "Backups")), proc, new FakeCapture(48, 30), new FakeSampler(99));
        var result = (await vm.MeasureAsync(game, seconds: 1, startDelaySeconds: 0))!;
        Assert.Equal(new TuningDecision(-1, "GsTuneLower"), result.Decision); Assert.True(result.CanApplyStep);
        Assert.Equal(-1, (await vm.AnalyzeSettingsAsync(game))!.PendingStep);
        Assert.Equal("GsCloseGame", await vm.ApplyStepAsync(game)); Assert.Equal(Ini, File.ReadAllText(config));
        proc.Run = false;
        Assert.Equal("GsStepLowered", await vm.ApplyStepAsync(game));
        Assert.Equal("2", new IniText(File.ReadAllText(config)).Get("ScalabilityGroups", "sg.ShadowQuality"));
        Assert.Equal("GsNoPendingStep", await vm.ApplyStepAsync(game));
        Assert.Equal("GsRestored", await vm.RestoreSettingsAsync(game)); Assert.Equal(Ini, File.ReadAllText(config));
        Assert.Null(await vm.MeasureAsync(game, 1, 0)); // game not running
    }

    [Fact, System.Runtime.Versioning.SupportedOSPlatform("windows")] public async Task RealGpuCounterIsReadable()
    {
        var load = await new PdhGpuLoadSampler().AverageAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        output.WriteLine($"GPU 3D load (2 s): {load?.ToString("0.0") ?? "unavailable"}");
        Assert.True(load is null or >= 0 and <= 100);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
