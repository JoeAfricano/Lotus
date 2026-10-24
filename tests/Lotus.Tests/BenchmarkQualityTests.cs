using System.Text.Json;
using Lotus.Core.Benchmark;
using Xunit;

namespace Lotus.Tests;

public sealed class BenchmarkQualityTests
{
    private static BenchmarkSession Session() => new(Guid.NewGuid(), new(1, "game.exe", DateTimeOffset.UtcNow),
        DateTimeOffset.UtcNow, 30, "Completed", "2.6.0", BenchmarkMath.Version, "raw.csv", "hash", "", "{}",
        new("1", 0, 0, 1001, [], new(1000, 10, 100, 100, 10, 10, 10, 0, 50), "Application", false), "");

    [Fact] public void LegacyPayloadRemainsUnknownAndCannotProveGain()
    {
        var session = JsonSerializer.Deserialize<BenchmarkSession>(JsonSerializer.Serialize(Session()))!;
        Assert.Equal(CaptureIntegrity.Unknown, session.Integrity);
        Assert.True(session.MathematicallyValid);
        Assert.False(BenchmarkQuality.IntegrityAllowsGainAssessment(session, Session()));
    }
    [Theory]
    [InlineData(null, null, null)] [InlineData(-1L, 0L, 0L)] [InlineData(0L, -1L, 0L)] [InlineData(0L, 0L, -1L)]
    public void MissingOrInvalidCountersNeverVerify(long? events, long? buffers, long? overflow)
    {
        var s = Session() with { CollectorExitCode = 0, EtwEvidence = new(events, buffers, overflow, "test", true) };
        Assert.Equal(CaptureIntegrity.Unknown, s.Integrity);
    }
    [Theory] [InlineData(1, 0, 0)] [InlineData(0, 1, 0)] [InlineData(0, 0, 1)]
    public void PositiveCounterDetectsLoss(long events, long buffers, long overflow)
    {
        var s = Session() with { EtwEvidence = new(events, buffers, overflow, "test", false) };
        Assert.Equal(CaptureIntegrity.LossDetected, s.Integrity);
        Assert.False(BenchmarkQuality.IntegrityAllowsGainAssessment(s, Session()));
    }
    [Fact] public void PeriodicZeroAndSuccessfulExitAreNotFinalEvidence()
    {
        var s = Session() with { CollectorExitCode = 0, EtwEvidence = new(0, 0, 0, "periodic", false) };
        Assert.Equal(CaptureIntegrity.Unknown, s.Integrity);
        Assert.Equal(CaptureIntegrity.Verified, (s with { EtwEvidence = new(0, 0, 0, "validated final source", true) }).Integrity);
        Assert.Equal(CaptureIntegrity.Invalid, (s with { CollectorExitCode = 6 }).Integrity);
        Assert.Equal(CaptureIntegrity.Invalid, (s with { Status = "Failed" }).Integrity);
        Assert.Equal(CaptureIntegrity.Invalid, (s with { Data = null }).Integrity);
    }
}

