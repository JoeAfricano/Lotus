using Lotus.Core.Tools;
using Xunit;

namespace Lotus.Tests;

public sealed class DriverNoticeTests
{
    private static readonly DateTime Today = new(2026, 11, 11);
    private static readonly GpuDriver Nvidia = new("NVIDIA GeForce RTX 4060", "32.0.15.7283", new(2025, 6, 1), "NVIDIA");

    [Fact] public void NvidiaNewerDriverIsToldOncePerVersion()
    {
        Assert.Equal("572.83", Nvidia.FriendlyVersion);
        var latest = new LatestDriver("GeForce Game Ready Driver", "581.57", new(2026, 10, 20), "https://www.nvidia.com/x");
        var first = DriverNotice.Decide(Nvidia, latest, null, Today);
        Assert.Equal((DriverNoticeKind.Newer, "581.57", "572.83", "nvidia:581.57"), (first.Kind, first.Latest, first.Installed, first.Key));
        Assert.Equal(DriverNoticeKind.None, DriverNotice.Decide(Nvidia, latest, "nvidia:581.57", Today).Kind);       // already told
        Assert.Equal(DriverNoticeKind.Newer, DriverNotice.Decide(Nvidia, latest with { Version = "582.10" }, "nvidia:581.57", Today).Kind);
        Assert.Equal(DriverNoticeKind.None, DriverNotice.Decide(Nvidia, latest with { Version = "572.83" }, null, Today).Kind); // up to date
        Assert.Equal(DriverNoticeKind.None, DriverNotice.Decide(Nvidia, null, null, Today).Kind);                    // offline
        Assert.True(DriverNotice.IsNewer("581.57", "572.83")); Assert.False(DriverNotice.IsNewer("572.8", "572.83")); Assert.False(DriverNotice.IsNewer("x", "1"));
    }

    [Fact] public void AmdAndIntelOnlyNoteAnOldDriver()
    {
        var amd = new GpuDriver("AMD Radeon RX 7600", "32.0.21013.1000", new(2026, 1, 10), "AMD");
        var old = DriverNotice.Decide(amd, null, null, Today);
        Assert.Equal((DriverNoticeKind.Old, DriverNotice.AmdUrl), (old.Kind, old.Url));
        Assert.Equal(DriverNoticeKind.None, DriverNotice.Decide(amd, null, old.Key, Today).Kind);
        Assert.Equal(DriverNoticeKind.None, DriverNotice.Decide(amd with { Date = new(2026, 9, 1) }, null, null, Today).Kind);
        Assert.Equal(DriverNoticeKind.None, DriverNotice.Decide(amd with { Vendor = "Other" }, null, null, Today).Kind);
        Assert.Equal(DriverNoticeKind.None, DriverNotice.Decide(null, null, null, Today).Kind);
    }
}
