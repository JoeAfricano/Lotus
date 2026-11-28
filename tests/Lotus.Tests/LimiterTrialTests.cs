using Lotus.Core.Benchmark;
using Lotus.Core.Games;
using Lotus.Infrastructure.Benchmark;
using Xunit;

namespace Lotus.Tests;

// Study of 25-28/11/2026: Reflex was the worst limiter in Stellar Blade (60 Hz) and the best in Wukong and Gothic.
public sealed class LimiterTrialTests
{
    private static List<FrameSample> Frames(params double[] ms)
    {
        var list = new List<FrameSample>(); var t = 0.0;
        foreach (var m in ms) { t += m / 1000; list.Add(new(t, m)); }
        return list;
    }
    private static LimiterTrialResult R(string mode, double offBeat, double? latency) => new(mode, offBeat, latency, latency + 6, 3600, DateTimeOffset.Now);

    [Fact]
    public void Frames_off_the_screens_beat_are_counted_per_minute()
    {
        var steady = Frames([.. Enumerable.Repeat(16.68, 3600)]);                       // 60 s at 59.95 Hz: all in rhythm
        Assert.Equal(0, LimiterTrial.OffBeatPerMinute(steady, 59.95));
        var doubled = Frames([.. Enumerable.Repeat(16.68, 3590), .. Enumerable.Repeat(33.36, 5)]); // 5 frames shown twice
        Assert.Equal(5, LimiterTrial.OffBeatPerMinute(doubled, 59.95), 0);
        var wobbly = Frames([.. Enumerable.Range(0, 3600).Select(i => i % 4 == 0 ? 8.0 : 16.68)]); // one in four out of the beat
        Assert.True(LimiterTrial.OffBeatPerMinute(wobbly, 59.95) > 800);
        Assert.Equal(0, LimiterTrial.OffBeatPerMinute([], 60));
    }

    [Fact]
    public void Picks_fewer_frames_off_the_beat_then_lower_latency()
    {
        // Stellar Blade at 60 Hz: Reflex ~900 per minute off the beat, front edge clean.
        Assert.Equal(LimiterTrial.FrontEdge, LimiterTrial.Pick([R(LimiterTrial.Reflex, 900, 60), R(LimiterTrial.FrontEdge, 3, 62)])!.Mode);
        // Gothic at 60 Hz: the same line (1 against 4.5 per minute), Reflex 46 ms against 70 ms.
        Assert.Equal(LimiterTrial.Reflex, LimiterTrial.Pick([R(LimiterTrial.Reflex, 1, 46), R(LimiterTrial.FrontEdge, 4.5, 70)])!.Mode);
        // The player's ceiling (70 ms) takes out a limiter above it, whatever its line.
        Assert.Equal(LimiterTrial.Reflex, LimiterTrial.Pick([R(LimiterTrial.Reflex, 40, 55), R(LimiterTrial.FrontEdge, 2, 75)], ceilingMs: 70)!.Mode);
        Assert.Null(LimiterTrial.Pick([R(LimiterTrial.FrontEdge, 2, 75)], ceilingMs: 70));
        Assert.Equal(LimiterTrial.FrontEdge, LimiterTrial.Pick([R(LimiterTrial.Reflex, 3, null), R(LimiterTrial.FrontEdge, 1, null)])!.Mode); // no Reflex: line only
    }

    [Fact]
    public void Each_candidate_is_measured_once_starting_with_the_one_in_use()
    {
        Assert.Equal(LimiterTrial.Reflex, LimiterTrial.Next(LimiterTrial.Reflex, null, reflexAvailable: true));
        var one = new Dictionary<string, LimiterTrialResult> { [LimiterTrial.Reflex] = R(LimiterTrial.Reflex, 1, 46) };
        Assert.Equal(LimiterTrial.FrontEdge, LimiterTrial.Next(LimiterTrial.Reflex, one, true)); // the other one, after a switch
        one[LimiterTrial.FrontEdge] = R(LimiterTrial.FrontEdge, 4, 70);
        Assert.Null(LimiterTrial.Next(LimiterTrial.FrontEdge, one, true));                      // both done
        Assert.Null(LimiterTrial.Next("back", null, true));                                     // set by hand: not a trial
        Assert.Equal([LimiterTrial.FrontEdge], LimiterTrial.Candidates(reflexAvailable: false)); // AMD/Intel or Vulkan
        Assert.Equal(PacingPresets.GameLimiter, LimiterTrial.Next(PacingPresets.GameLimiter, null, true)); // the low-latency preset is measured too
    }

    [Fact]
    public void Latency_median_and_p95_need_enough_frames()
    {
        var (median, p95) = LimiterTrial.Latency([.. Enumerable.Range(1, 100).Select(i => (double)i)]);
        Assert.Equal(51, median); Assert.Equal(95, p95);
        Assert.Equal((null, null), LimiterTrial.Latency([40, 41, 42]));
    }

    [Fact]
    public void PresentMon_pc_latency_column_is_read_per_frame()
    {
        var path = Path.Combine(Path.GetTempPath(), "lotus-pcl-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var rows = new List<string> { "Application,ProcessID,SwapChainAddress,FrameType,TimeInSeconds,MsBetweenPresents,MsPCLatency,MsCPUBusy,MsGPUBusy" };
            for (var i = 0; i < 40; i++) rows.Add(FormattableString.Invariant($"game.exe,42,0x1,Application,{i * 0.0167:0.0000},16.7,{(i % 2 == 0 ? 45.5 : 47.5)},12.5,9.25"));
            rows.Add("game.exe,42,0x1,Application,0.7000,16.7,NA,NA,NA"); // no marker on this frame
            File.WriteAllLines(path, rows);
            var parsed = PresentMonCsv.Read(path, 42);
            Assert.Equal(40, parsed.PcLatencies.Count);
            Assert.Equal((47.5, 47.5), LimiterTrial.Latency(parsed.PcLatencies));
            Assert.Equal(parsed.Frames.Count, parsed.Busy.Count); Assert.Equal(new FrameBusy(12.5, 9.25), parsed.Busy[0]); Assert.Equal(new FrameBusy(null, null), parsed.Busy[^1]);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(70, 70)][InlineData(0, 0)][InlineData(5, 0)][InlineData(500, 0)]
    public void Latency_ceiling_stays_in_a_sane_range(int value, int expected) =>
        Assert.Equal(expected, new Lotus.Core.InterfacePreferences { LatencyCeilingMs = value }.Normalize().LatencyCeilingMs);
}
