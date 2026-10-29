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
        var rtx4090 = new SystemProfile("NVIDIA GeForce RTX 4090", 24576, 32, 64_000, 2560, 1440, 144);
        Assert.Equal(141, SettingsAdvisor.Recommend(rtx4090, "competitive", adaptiveSync: true).FpsCap);   // VRR: 3 below
        Assert.Equal(144, SettingsAdvisor.Recommend(rtx4090, "competitive").FpsCap);                        // fixed rate: the rate itself
        var weak = SettingsAdvisor.Recommend(new("NVIDIA GeForce GTX 1650", 4096, 4, 8_000, 1920, 1080, 75));
        Assert.Equal(37.5, weak.PacedCap); Assert.Contains("GsReasonHalfRate", weak.ReasonKeys);           // cannot hold 75: exact half
        Assert.Equal(60, SettingsAdvisor.Recommend(new("NVIDIA GeForce GTX 1650", 4096, 4, 8_000, 1920, 1080, 144), "balanced", adaptiveSync: true).FpsCap);
    }

    [Fact] public void EdidRevealsNameRangeAndFreeSync()
    {
        var edid = new byte[256];
        edid[24] = 0x01;                                                                       // continuous frequency
        edid[72 + 3] = 0xFC; "LG ULTRAGEAR\n"u8.ToArray().CopyTo(edid, 72 + 5);                // monitor name
        edid[90 + 3] = 0xFD; edid[90 + 5] = 48; edid[90 + 6] = 144;                            // 48–144 Hz
        edid[128] = 0x02; edid[130] = 20; edid[132] = (3 << 5) | 3; edid[133] = 0x1A; edid[134] = 0; edid[135] = 0; // AMD vendor block
        var (name, min, max, freeSync, continuous) = DisplaySync.ParseEdid(edid);
        Assert.Equal(("LG ULTRAGEAR", 48, 144, true, true), (name, min, max, freeSync, continuous));
        Assert.True(new DisplaySync("x", 75, true, false, "nvapi").VrrAvailableButOff);
        var dp = new byte[256]; dp[128] = 0x70; dp[130] = 12; dp[133] = 0x2B; dp[135] = 6;   // DisplayID with an Adaptive-Sync block
        Assert.True(DisplaySync.ParseEdid(dp).FreeSync);
        Assert.False(DisplaySync.ParseEdid(new byte[256]).FreeSync);                          // no blocks: no VRR claimed
    }
}
