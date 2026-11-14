using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

// The real files of the player's MSI Afterburner (RTX 5060, 127-point curve) before Lotus saved anything.
public sealed class AfterburnerProfileFileTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private static string DefaultCurve => AfterburnerProfileFile.Get(Fixture("afterburner-card.cfg"), "Defaults", "VFCurve")!;

    [Fact]
    public void Reads_the_real_default_curve()
    {
        var curve = AfterburnerProfileFile.Decode(DefaultCurve)!;
        Assert.Equal(127, AfterburnerProfileFile.Points(curve));
        Assert.All(AfterburnerProfileFile.Offsets(DefaultCurve)!, o => Assert.Equal(0, o));
    }

    [Fact]
    public void Offsets_round_trip_and_nothing_else_in_the_curve_changes()
    {
        var offsets = Enumerable.Range(0, 127).Select(i => i < 80 ? 349_000 : 274_000 - (i - 80) * 13_000).ToArray();
        var written = AfterburnerProfileFile.WithOffsets(DefaultCurve, offsets)!;
        Assert.Equal(offsets, AfterburnerProfileFile.Offsets(written));
        Assert.Equal(DefaultCurve.Length, written.Length);
        var before = AfterburnerProfileFile.Decode(DefaultCurve)!; var after = AfterburnerProfileFile.Decode(written)!;
        for (var i = 0; i < before.Length; i++)
        {
            var inOffset = i >= 12 && i < 12 + 127 * 12 && (i - 12) % 12 >= 8;
            if (!inOffset) Assert.Equal(before[i], after[i]);
        }
    }

    [Fact]
    public void Wrong_point_count_is_refused()
    {
        Assert.Null(AfterburnerProfileFile.WithOffsets(DefaultCurve, new int[100]));
        Assert.Null(AfterburnerProfileFile.WithProfiles(Fixture("afterburner-card.cfg"), new(100, 0, 0, new int[100])));
    }

    [Fact]
    public void Profile_1_is_stock_and_profile_2_is_the_tune()
    {
        var card = Fixture("afterburner-card.cfg");
        var offsets = Enumerable.Repeat(150_000, 127).ToArray();
        var text = AfterburnerProfileFile.WithProfiles(card, new(100, 150_000, 500_000, offsets))!;
        Assert.Equal(DefaultCurve, AfterburnerProfileFile.Get(text, "Profile1", "VFCurve"));
        Assert.Equal("0", AfterburnerProfileFile.Get(text, "Profile1", "CoreClkBoost"));
        Assert.Equal("150000", AfterburnerProfileFile.Get(text, "Profile2", "CoreClkBoost"));
        Assert.Equal("500000", AfterburnerProfileFile.Get(text, "Profile2", "MemClkBoost"));
        Assert.Equal(offsets, AfterburnerProfileFile.Offsets(AfterburnerProfileFile.Get(text, "Profile2", "VFCurve")!));
        // Everything Afterburner already had stays as it was.
        foreach (var s in new[] { "Startup", "PreSuspendedMode", "Defaults", "Settings" })
            foreach (var k in new[] { "Format", "PowerLimit", "VFCurve", "FanMode", "CaptureDefaults" })
                Assert.Equal(AfterburnerProfileFile.Get(card, s, k), AfterburnerProfileFile.Get(text, s, k));
        // Saving again replaces the profiles instead of adding copies.
        var again = AfterburnerProfileFile.WithProfiles(text, new(100, 0, 0, new int[127]))!;
        Assert.Equal(1, again.Split("[Profile2]").Length - 1);
        Assert.All(AfterburnerProfileFile.Offsets(AfterburnerProfileFile.Get(again, "Profile2", "VFCurve")!)!, o => Assert.Equal(0, o));
    }

    [Fact]
    public void Auto_switch_sets_only_the_2D_and_3D_keys()
    {
        var main = Fixture("afterburner-main.cfg");
        var text = AfterburnerProfileFile.WithAutoSwitch(main);
        Assert.Equal("0", AfterburnerProfileFile.Get(text, "Settings", "Profile2D")); // Profile 1
        Assert.Equal("1", AfterburnerProfileFile.Get(text, "Settings", "Profile3D")); // Profile 2
        var changed = main.Split('\n').Zip(text.Split('\n')).Count(p => p.First != p.Second);
        Assert.Equal(2, changed);
        Assert.Equal(main.Split('\n').Length, text.Split('\n').Length);
    }

    [Fact]
    public void Live_curve_check_allows_one_MHz()
    {
        Assert.True(AfterburnerProfileFile.Matches([150_000, 0], [150_400, 0]));
        Assert.False(AfterburnerProfileFile.Matches([150_000, 0], [165_000, 0]));
        Assert.False(AfterburnerProfileFile.Matches([0], [0, 0]));
    }
}
