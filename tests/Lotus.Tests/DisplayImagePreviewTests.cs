using Lotus.Core.Display;
using Xunit;

namespace Lotus.Tests;

public sealed class DisplayImagePreviewTests
{
    private static CalibrationState Baseline() => new("monitor", "display", "Monitor", false, new(65, 0, 100), new(60, 0, 100), null, new(50, 0, 100, 50));
    [Fact] public void NeutralDraftPreservesPixelsAndAlphaWithinOneEncodingLevel()
    {
        var input = Enumerable.Range(0, 1024).Select(i => (byte)(i % 256)).ToArray();
        var output = DisplayImagePreview.Render(input, new(65, 60, Saturation: 50), Baseline());
        for (var i = 0; i < input.Length; i++) Assert.InRange(Math.Abs(output[i] - input[i]), 0, i % 4 == 3 ? 0 : 1);
        Assert.NotSame(input, output);
    }
    [Fact] public void ZeroSaturationMakesColorGrayAndKeepsSourceUnmodified()
    {
        byte[] source = [20, 80, 220, 255]; var output = DisplayImagePreview.Render(source, new(Saturation: 0), Baseline());
        Assert.Equal(output[0], output[1]); Assert.Equal(output[1], output[2]); Assert.Equal((byte)220, source[2]);
    }
    [Fact] public void SaturationDoesNotTintNeutralGray()
    {
        byte[] source = [128, 128, 128, 255]; var output = DisplayImagePreview.Render(source, new(Saturation: 100), Baseline());
        Assert.Equal(output[0], output[1]); Assert.Equal(output[1], output[2]);
    }
    [Fact] public void BrighterDraftAndGammaRevealMidtonesWithoutAnyDriverDependency()
    {
        byte[] source = [80, 80, 80, 255];
        var brighter = DisplayImagePreview.Render(source, new(Brightness: 85, Gamma: 1.1), Baseline());
        var darker = DisplayImagePreview.Render(source, new(Brightness: 45), Baseline());
        Assert.True(brighter[0] > source[0]); Assert.True(darker[0] < source[0]);
    }
    [Fact] public void WarmthMovesRedAndBlueInOppositeDirections()
    {
        byte[] source = [128, 128, 128, 255]; var output = DisplayImagePreview.Render(source, new(Warmth: 10), Baseline());
        Assert.True(output[2] > output[0]); Assert.Equal((byte)255, output[3]);
    }
    [Fact] public void PreviewStepsAreLargeEnoughToSeeForTypicalSuggestions()
    {
        byte[] source = [128, 128, 128, 255];
        var dimmer = DisplayImagePreview.Render(source, new(Brightness: 45), Baseline());
        Assert.True(source[0] - dimmer[0] >= 15);
        var warm = DisplayImagePreview.Render(source, new(Warmth: 8), Baseline());
        Assert.True(warm[2] - warm[0] >= 6);
        byte[] color = [40, 90, 200, 255];
        var vivid = DisplayImagePreview.Render(color, new(Saturation: 75), Baseline());
        Assert.True(vivid[2] - vivid[0] > color[2] - color[0]);
    }
    [Fact] public void MalformedBuffersAndProfilesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => DisplayImagePreview.Render([1, 2, 3], new(), Baseline()));
        Assert.Throws<ArgumentException>(() => DisplayImagePreview.Render([0, 0, 0, 255], new(Gamma: double.NaN), Baseline()));
        Assert.Throws<ArgumentException>(() => DisplayImagePreview.RenderInto(new byte[4], new byte[8], new(), Baseline()));
    }
    [Fact] public void AlreadyAppliedCorrectionIsNotAppliedTwiceAndOppositeExtremesRemainSupported()
    {
        byte[] source = [80, 128, 170, 255]; var applied = new CalibrationProfile(65, 60, 1.3, Warmth: 10, Tint: 5);
        var neutral = DisplayImagePreview.Render(source, applied, Baseline(), applied);
        for (var i = 0; i < 4; i++) Assert.InRange(Math.Abs(source[i] - neutral[i]), 0, 1);
        var reversed = DisplayImagePreview.Render(source, applied with { Gamma = .75, Warmth = -10 }, Baseline(), applied);
        Assert.NotEqual(source, reversed);
    }
}
