using Lotus.Core.Games;
using Xunit;

namespace Lotus.Tests;

// Gothic 1 Remake, 28/11/2026: FrameGenerationDLSS=1 did not turn the generator on; only the frame count showed it.
public sealed class FrameGenCheckTests
{
    [Theory]
    [InlineData(74.9, 37.5, true)]    // 2X at 75 Hz: twice the real frames
    [InlineData(59.9, 20.0, true)]    // 3X
    [InlineData(71.0, 71.2, false)]   // off: about the same
    [InlineData(60.0, 42.0, null)]    // in between: no verdict
    [InlineData(60.0, null, null)]    // no Reflex timings
    public void Shown_against_real_frames_tells_the_generator(double shown, double? real, bool? expected) => Assert.Equal(expected, FrameGenCheck.Seen(shown, real));

    [Fact]
    public void PresentMon_frame_type_counts_only_when_it_names_a_generator()
    {
        Assert.True(FrameGenCheck.Seen(60, null, "AMD AFMF, Application"));
        Assert.Null(FrameGenCheck.Seen(60, null, "Application"));      // Gothic: generated frames also came as Application
        Assert.Null(FrameGenCheck.Seen(60, null, "rtss-pipe"));        // RivaTuner capture, not a frame type
        Assert.False(FrameGenCheck.Seen(60, 60, "Application, Repeated"));
    }

    [Fact]
    public void The_choice_and_the_measurement_must_agree()
    {
        Assert.Equal("FgChosenButOff", FrameGenCheck.Mismatch(true, false));
        Assert.Equal("FgOnButNotChosen", FrameGenCheck.Mismatch(false, true));
        Assert.Null(FrameGenCheck.Mismatch(true, true));
        Assert.Null(FrameGenCheck.Mismatch(true, null)); // not measured: nothing to say
    }
}