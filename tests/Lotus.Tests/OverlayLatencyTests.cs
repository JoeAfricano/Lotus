using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

// Item 9 (28/11/2026): the latency line on the player's Neon card, smallest change possible, off by default.
[Collection(OverlayLayoutStatics.Name)]
public sealed class OverlayLatencyTests
{
    [Fact]
    public void Latency_line_only_adds_to_the_neon_layout()
    {
        var original = LotusOverlayLayout.Text(LotusOverlayLayout.PremiumNeon);
        var added = LotusOverlayLayout.WithLatency(original);
        var head = original[..original.IndexOf("[Source0]", StringComparison.Ordinal)];
        Assert.Contains("Sources=20", added); Assert.Contains("Layers=18", added);           // 17 + 3 sources, 16 + 2 layers
        var expectedHead = head.Replace("Sources=17", "Sources=20").Replace("Layers=16", "Layers=18");
        Assert.StartsWith(expectedHead, added);                                                // nothing else in the header moved
        Assert.StartsWith(original[original.IndexOf("[Source0]", StringComparison.Ordinal)..].TrimEnd(), added[expectedHead.Length..]); // every original line kept, in order
        Assert.Contains("Formula=reflexlatency(0,12)", added);
        Assert.Contains("VisibilitySource=Lotus Latency Missing\r\nText=LAT \u0097", added);    // "—" when the game has no Reflex
        Assert.Contains("[Layer17]", added); Assert.Contains("[Source19]", added);
    }

    [Fact]
    public void Off_exports_the_layout_byte_for_byte_and_on_adds_the_line()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lotus-ovl-" + Guid.NewGuid().ToString("N"));
        try
        {
            LotusOverlayLayout.LatencyLine = false;
            var off = File.ReadAllBytes(LotusOverlayLayout.Export(dir, LotusOverlayLayout.PremiumNeon));
            Assert.Equal(System.Text.Encoding.Latin1.GetBytes(LotusOverlayLayout.Text(LotusOverlayLayout.PremiumNeon)), off);
            LotusOverlayLayout.LatencyLine = true;
            var on = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(LotusOverlayLayout.Export(dir, LotusOverlayLayout.PremiumNeon)));
            Assert.Contains("LAT %Lotus Latency% ms", on);
            Assert.True(File.ReadAllBytes(Path.Combine(dir, "Lotus-Premium-neon.ovl")).AsSpan().IndexOf(new byte[] { 0x4C, 0x41, 0x54, 0x20, 0x97 }) >= 0); // "LAT —" in the ANSI file
            var other = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(LotusOverlayLayout.Export(dir, LotusOverlayLayout.Main)));
            Assert.DoesNotContain("Lotus Latency", other);                                     // only the Neon card gets it
            var preview = RtssLayoutPreview.Render(LotusOverlayLayout.PremiumNeon);
            Assert.True(preview.Png.Length > 1000);
            if (Environment.GetEnvironmentVariable("LOTUS_DUMP_PREVIEW") is { Length: > 0 } dump) File.WriteAllBytes(dump, preview.Png);
        }
        finally { LotusOverlayLayout.LatencyLine = false; Directory.Delete(dir, true); }
    }
}

// LotusOverlayLayout.LatencyLine is static: these tests switch it, so nothing else may export a layout at the same time.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OverlayLayoutStatics { public const string Name = "OverlayLayoutStatics"; }
