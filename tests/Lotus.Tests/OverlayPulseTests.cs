using Lotus.Core.Benchmark;
using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

public sealed class OverlayPulseTests
{
    private static string Sources => "[Source0]\r\nName=Frametime\r\n[Source1]\r\nName=GPU Load\r\n";

    [Fact] public void Pulse_uses_the_theme_colors_and_draws_the_limit_line()
    {
        var text = LotusPulseLayout.Build(Sources, new("AABBCC", "DDEEFF"), 74.97);
        Assert.Contains("Name=Lotus Pulse", text);
        Assert.Contains("Sources=2", text);
        Assert.Contains("TextColor=AABBCC", text);
        Assert.Contains("TextColor=DDEEFF", text);
        Assert.Contains("Name=Limit line", text);
        Assert.Contains("Text=13.3", text); // 1000 / 74.97
        Assert.Contains($"PositionY={LotusPulseLayout.LineY(1000 / 74.97)}", text);
        Assert.DoesNotContain("Label 16", text); // 13.3 ms sits next to 16.7 ms: only the limit is labeled
        Assert.Contains("Label 16", LotusPulseLayout.Build(Sources, LotusPulseLayout.Default, 144));
    }

    [Fact] public void Pulse_without_a_limit_has_no_limit_line_and_labels_stay_ascii()
    {
        var text = LotusPulseLayout.Build(Sources, LotusPulseLayout.Default, null);
        Assert.DoesNotContain("Limit line", text);
        // RivaTuner's overlay font drops accented letters: only the degree sign (shown fine in game) is allowed.
        Assert.All(text, ch => Assert.True(ch < 128 || ch == '°', $"non-ASCII '{ch}'"));
    }

    [Fact] public void Graph_lines_sit_inside_the_graph()
    {
        Assert.Equal(LotusPulseLayout.GraphTop, LotusPulseLayout.LineY(33.3));
        Assert.Equal(LotusPulseLayout.GraphTop + LotusPulseLayout.GraphHeight, LotusPulseLayout.LineY(0));
        Assert.InRange(LotusPulseLayout.LineY(16.7), LotusPulseLayout.GraphTop + 27, LotusPulseLayout.GraphTop + 29);
        Assert.Equal(LotusPulseLayout.GraphTop, LotusPulseLayout.LineY(100)); // clamped
    }

    [Fact] public void Pulse_export_follows_the_colors_set_by_the_app()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lotus-pulse-" + Guid.NewGuid().ToString("N"));
        var before = LotusOverlayLayout.PulseColors;
        try
        {
            LotusOverlayLayout.PulseColors = new("112233", "445566");
            var file = LotusOverlayLayout.Export(dir, LotusOverlayLayout.Pulse);
            Assert.EndsWith("Lotus-Pulse.ovl", file);
            Assert.Contains("TextColor=112233", File.ReadAllText(file, System.Text.Encoding.Latin1));
            Assert.False(LotusOverlayLayout.RefreshGenerated(dir)); // same colors: nothing to renew
            LotusOverlayLayout.PulseColors = new("778899", "445566");
            Assert.True(LotusOverlayLayout.RefreshGenerated(dir));
            Assert.Contains("TextColor=778899", File.ReadAllText(file, System.Text.Encoding.Latin1));
            Assert.Equal("pulse", LotusOverlayLayout.VariantOf("Lotus-Pulse"));
        }
        finally { LotusOverlayLayout.PulseColors = before; Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("Hardware: Independent Flip", PresentPath.Direct)]
    [InlineData("Hardware Composed: Independent Flip", PresentPath.Direct)]
    [InlineData("Hardware: Legacy Flip", PresentPath.Direct)]
    [InlineData("Composed: Flip", PresentPath.Composed)]
    [InlineData("Composed: Copy with GPU GDI", PresentPath.Composed)]
    [InlineData("Other", PresentPath.Unknown)]
    [InlineData(null, PresentPath.Unknown)]
    public void Present_modes_are_classified(string? mode, PresentPath path) => Assert.Equal(path, PresentModes.Classify(mode));

    [Theory]
    [InlineData(5, 4, 4, MpoStatus.SwitchedOff)]
    [InlineData(null, 4, 2, MpoStatus.Supported)]
    [InlineData(null, 1, 1, MpoStatus.NoOverlayPlane)]
    [InlineData(null, 4, 1, MpoStatus.NoOverlayPlane)]
    [InlineData(null, null, null, MpoStatus.Unknown)]
    public void Mpo_needs_a_plane_besides_the_desktop(int? testMode, int? planes, int? rgb, MpoStatus expected) =>
        Assert.Equal(expected, PresentModes.Mpo(testMode, planes, rgb));

    [Fact] public void Present_mode_of_the_measured_swap_chain_is_kept()
    {
        var csv = Path.Combine(Path.GetTempPath(), "lotus-pm-" + Guid.NewGuid().ToString("N") + ".csv");
        var rows = new List<string> { "Application,ProcessID,SwapChainAddress,PresentMode,TimeInMs,MsBetweenPresents" };
        for (var i = 0; i < 50; i++) rows.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"game.exe,42,0xA,{(i < 45 ? "Hardware: Independent Flip" : "Composed: Flip")},{i * 13.3:0.0},13.3"));
        File.WriteAllLines(csv, rows);
        try { Assert.Equal("Hardware: Independent Flip", Lotus.Infrastructure.Benchmark.PresentMonCsv.Read(csv, 42).PresentMode); }
        finally { File.Delete(csv); }
    }
}
