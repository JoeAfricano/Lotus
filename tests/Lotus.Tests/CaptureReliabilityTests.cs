using Lotus.Infrastructure.Benchmark;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class CaptureReliabilityTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-etw-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    [Fact] public void LedgerKeepsOnlyLotusSessionsUntilClosed()
    {
        var ledger = new EtwSessionLedger(dir);
        Assert.Empty(ledger.Pending());
        var a = "Lotus-" + Guid.NewGuid().ToString("N"); var b = "Lotus-" + Guid.NewGuid().ToString("N");
        ledger.Add(a); ledger.Add(b); ledger.Add(a);
        Assert.Equal([b, a], ledger.Pending());
        File.AppendAllLines(Path.Combine(dir, "open-etw-sessions.txt"), ["NT Kernel Logger", "Lotus-x"]); // never another tool's session
        Assert.Equal([b, a], ledger.Pending());
        ledger.Remove(a); ledger.Remove(b);
        Assert.Empty(ledger.Pending());
        Assert.False(File.Exists(Path.Combine(dir, "open-etw-sessions.txt")));
    }

    [Fact] public void FirstFrameCheckNeedsARowAfterTheHeader()
    {
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "frames.csv");
        Assert.False(PresentMonCsv.HasRows(csv));
        File.WriteAllText(csv, "Application,ProcessID,MsBetweenPresents\n");
        Assert.False(PresentMonCsv.HasRows(csv));
        File.AppendAllText(csv, "game.exe,1,16.6\n");
        Assert.True(PresentMonCsv.HasRows(csv));
    }

    [Fact] public void RivaTunerFramesBecomeATimeline()
    {
        var t = RtssPreferredCaptureProvider.Timeline([16.7, 16.7, 120, double.NaN, 16.6]);
        Assert.Equal(4, t.Count);
        Assert.Equal(0.0334, t[1].TimeSeconds, 4);
        Assert.Equal(120, t[2].Milliseconds);
        Assert.Equal(0.1700, t[3].TimeSeconds, 4);
    }
}
