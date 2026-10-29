using Lotus.Core.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class GameIntelTests
{
    private static readonly SystemProfile Rtx5060 = new("NVIDIA GeForce RTX 5060", 8192, 12, 16_000, 1920, 1080, 75);

    [Fact] public void DetectsUpscalersFrameGenAndEngineFromFileNames()
    {
        var t = GameTech.FromFiles(["Game-Win64-Shipping.exe", "nvngx_dlss.dll", "nvngx_dlssg.dll", "amd_fidelityfx_dx12.dll", "libxess.dll", "readme.txt"]);
        Assert.True(t.Dlss && t.FrameGen && t.Fsr && t.Xess); Assert.Equal("Unreal Engine", t.Engine);
        Assert.Equal("Unity", GameTech.FromFiles(["UnityPlayer.dll"]).Engine);
        Assert.False(GameTech.FromFiles(["game.exe"]).AnyUpscaler);
    }

    [Fact] public void TipsFollowWhatTheGameShipsAndTheMeasurement()
    {
        var r = SettingsAdvisor.Recommend(Rtx5060);
        var dlss = InGameAdvisor.Advise(new(true, true, true, false, null), r, Rtx5060);
        Assert.Equal("IgDlss", dlss[0].Key);                                     // RTX + DLSS in the game: DLSS first, not FSR
        Assert.DoesNotContain(dlss, t => t.Key == "IgFsr");
        Assert.Contains(dlss, t => t.Key == "IgFrameGen");                       // RTX 50 supports frame generation
        var fsrOnly = InGameAdvisor.Advise(new(false, false, true, false, null), r, new("AMD Radeon RX 6600", 8192, 12, 16_000, 1920, 1080, 75));
        Assert.Contains(fsrOnly, t => t.Key == "IgFsr");
        var cpu = InGameAdvisor.Advise(GameTech.Unknown, r, Rtx5060, [new("WhyCpuLimit", StutterFix.None, 55)]);
        Assert.Equal("IgCpuHeavy", cpu[0].Key);                                  // measured CPU limit comes first
        Assert.Contains(InGameAdvisor.Advise(GameTech.Unknown, r, Rtx5060, null, "competitive"), t => t.Key == "IgCompetitive");
        Assert.DoesNotContain(InGameAdvisor.Advise(new(true, true, false, false, null), r, Rtx5060, null, "competitive"), t => t.Key == "IgFrameGen"); // adds latency
    }

    [Fact] public void FpsCapFollowsTheMonitor()
    {
        Assert.Equal(141, SettingsAdvisor.Recommend(new("NVIDIA GeForce RTX 4090", 24576, 32, 64_000, 2560, 1440, 144), "competitive").FpsCap);
        Assert.Equal(60, SettingsAdvisor.Recommend(new("NVIDIA GeForce GTX 1650", 4096, 4, 8_000, 1920, 1080, 144)).FpsCap); // cannot feed 144 Hz: steady 60
    }
}
