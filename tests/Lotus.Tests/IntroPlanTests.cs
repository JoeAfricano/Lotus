using Lotus.Core.Brand;
using Xunit;

namespace Lotus.Tests;

public sealed class IntroPlanTests
{
    private static IntroKind Decide(bool enabled = true, bool reduce = false, string? seen = null, string current = "1.9.0-alpha",
        bool minimized = false, bool restarted = false, bool dev = false, bool skip = false, bool force = false)
        => IntroPlan.Decide(enabled, reduce, seen, current, minimized, restarted, dev, skip, force);

    [Fact] public void FirstRunPlaysTheFullIntro() => Assert.Equal(IntroKind.Full, Decide(seen: null));
    [Fact] public void AfterAnUpdatePlaysTheFullIntroAgain() => Assert.Equal(IntroKind.Full, Decide(seen: "1.8.1-alpha"));
    [Fact] public void SameVersionPlaysTheShortCut() => Assert.Equal(IntroKind.Short, Decide(seen: "1.9.0-ALPHA"));
    [Fact] public void TurnedOffInSettingsPlaysNothing() => Assert.Equal(IntroKind.None, Decide(enabled: false));
    [Fact] public void ReduceMotionPlaysNothing() => Assert.Equal(IntroKind.None, Decide(reduce: true));
    [Fact] public void StartedMinimizedOrRestartedPlaysNothing()
    {
        Assert.Equal(IntroKind.None, Decide(minimized: true));
        Assert.Equal(IntroKind.None, Decide(restarted: true));
        Assert.Equal(IntroKind.None, Decide(skip: true));
    }
    [Fact] public void DevInstanceOnlyWhenForced()
    {
        Assert.Equal(IntroKind.None, Decide(dev: true));
        Assert.Equal(IntroKind.Full, Decide(dev: true, force: true));
    }
}
