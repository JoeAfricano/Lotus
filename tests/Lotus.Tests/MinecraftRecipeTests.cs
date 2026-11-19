using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

// Based on the real Modrinth "Fabulously Optimized" options.txt layout (Fabric + Sodium + Iris).
public sealed class MinecraftRecipeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusMinecraft-" + Guid.NewGuid());
    private const string Options = "version:3955\r\nautoJump:false\r\nrenderDistance:14\r\nsimulationDistance:14\r\nentityDistanceScaling:1.0\r\nmaxFps:260\r\nenableVsync:true\r\nresourcePacks:[\"vanilla\",\"fabric\"]\r\nkey_key.attack:key.mouse.left\r\n";
    private static SettingsRecommendation Rec(QualityTier tier, int cap) => new(tier, tier, Upscaler.None, UpscaleMode.Off, cap, cap, 145, []);
    private static readonly GameEntry Game = new("manual", "mc", "Minecraft");

    private string Profile(string name, string text, DateTime? when = null)
    {
        var dir = Path.Combine(root, "AppData", "ModrinthApp", "profiles", name); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "options.txt"); File.WriteAllText(path, text);
        if (when is { } w) File.SetLastWriteTimeUtc(path, w);
        return path;
    }
    private MinecraftJavaRecipe Recipe() => new(Path.Combine(root, "AppData"), Path.Combine(root, "User"));

    [Fact] public void ChangesOnlyFpsAndDistancesAndNeverRaisesViewDistance()
    {
        var recipe = Recipe();
        var ultra = new ColonOptions(recipe.Transform(Options, Rec(QualityTier.Ultra, 75)));
        Assert.Equal("75", ultra.Get("maxFps"));
        Assert.Equal("14", ultra.Get("renderDistance")); // Ultra keeps the player's 14, never goes up to 32
        var medium = recipe.Transform(Options, Rec(QualityTier.Medium, 60));
        var o = new ColonOptions(medium);
        Assert.Equal("12", o.Get("renderDistance"));
        Assert.Equal("12", o.Get("simulationDistance"));
        Assert.Equal("true", o.Get("enableVsync"));
        Assert.Contains("resourcePacks:[\"vanilla\",\"fabric\"]\r\n", medium);
        Assert.Contains("key_key.attack:key.mouse.left", medium);
        Assert.Equal(Options.Split("\r\n").Length, medium.Split("\r\n").Length);
    }

    [Fact] public void StepMovesTwoChunksWithinLimits()
    {
        var recipe = Recipe();
        var down = new ColonOptions(recipe.StepQuality(Options, -1)!);
        Assert.Equal("12", down.Get("renderDistance"));
        Assert.Equal("12", down.Get("simulationDistance"));
        Assert.Equal("16", new ColonOptions(recipe.StepQuality(Options, +1)!).Get("renderDistance"));
        Assert.Null(recipe.StepQuality(Options.Replace("renderDistance:14", "renderDistance:4"), -1));
    }

    [Fact] public void FindsTheProfilePlayedLastAndIgnoresOtherGames()
    {
        var recipe = Recipe();
        Assert.Null(recipe.Probe(new GameEntry("steam", "1", "Minecraft Dungeons")));
        Assert.False(recipe.Probe(Game)!.CanApply);
        Profile("Old", Options, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var recent = Profile("Fabulously Optimized", Options.Replace("renderDistance:14", "renderDistance:10"), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var support = recipe.Probe(Game)!;
        Assert.True(support.CanApply);
        Assert.Equal(recent, support.Detail);
        recipe.WriteRaw(Game, recipe.Transform(recipe.ReadRaw(Game)!, Rec(QualityTier.Low, 75)));
        Assert.Contains("renderDistance:8", File.ReadAllText(recent));
        Assert.Contains("maxFps:75", File.ReadAllText(recent));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
