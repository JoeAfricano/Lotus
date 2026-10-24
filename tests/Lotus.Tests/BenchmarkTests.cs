using Lotus.Core.Benchmark;
using Lotus.Infrastructure.Benchmark;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lotus.Tests;

public sealed class BenchmarkTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LotusBenchmarkTests-" + Guid.NewGuid());
    public BenchmarkTests() => Directory.CreateDirectory(directory);
    private static FrameSample[] Constant(int count = 1000, double dt = 10) => Enumerable.Range(1, count).Select(i => new FrameSample(i * dt / 1000, dt)).ToArray();
    private static BenchmarkSession Session() => new(Guid.NewGuid(), new(42, "C:/Games/game.exe", DateTimeOffset.UtcNow), DateTimeOffset.UtcNow,
        10, "Completed", "2.6.0", BenchmarkMath.Version, "raw.csv", "hash", "1080p", "{}",
        new("0x1", 0, 0, 1001, Constant(), BenchmarkMath.Calculate(Constant()), "Application", false), "");
    [Fact] public void ConstantCadenceHasExactStatistics()
    {
        var s = BenchmarkMath.Calculate(Constant())!;
        Assert.Equal(100, s.AverageFps); Assert.Equal(100, s.Low1Fps); Assert.Equal(10, s.P95Ms);
        Assert.Equal(10, s.P99Ms); Assert.Equal(0, s.SpikeCount); Assert.Equal(10, s.DurationSeconds);
    }
    [Fact] public void SlowTailIsNotReciprocalPercentile()
    {
        var data = Constant(); for (var i = 990; i < 1000; i++) data[i] = data[i] with { Milliseconds = 100 };
        var s = BenchmarkMath.Calculate(data)!;
        Assert.Equal(10, s.Low1Fps); Assert.Equal(10, s.P99Ms); Assert.Equal(10, s.SpikeCount);
        Assert.Equal(1000000d / 10900, s.AverageFps, 8);
    }
    [Theory][InlineData(999, 20)][InlineData(1000, 1)]
    public void InsufficientCountOrDurationHasNoMetrics(int count, double dt) => Assert.Null(BenchmarkMath.Calculate(Constant(count, dt)));
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(-1)][InlineData(0)]
    public void InvalidIntervalHasNoMetrics(double value)
    { var data = Constant(); data[4] = data[4] with { Milliseconds = value }; Assert.Null(BenchmarkMath.Calculate(data)); }
    [Fact] public void ComparisonRequiresSameExecutableAndVersions()
    {
        var a = Session(); var b = Session(); Assert.True(BenchmarkMath.Comparable(a,b));
        Assert.False(BenchmarkMath.Comparable(a,a)); Assert.False(BenchmarkMath.Comparable(a,b with { Status = "Failed" }));
        Assert.False(BenchmarkMath.Comparable(a,b with { AlgorithmVersion = "other" }));
        Assert.False(BenchmarkMath.Comparable(a,b with { Target = b.Target with { Executable = "other.exe" } }));
        Assert.Equal(new MetricDifference(10, 10.000000000000009), MetricDifference.Between(100,110));
    }
    private string Csv(IEnumerable<string> rows)
    {
        var file = Path.Combine(directory, Guid.NewGuid()+".csv");
        File.WriteAllLines(file, new[] { "Application,ProcessID,SwapChainAddress,TimeInSeconds,MsBetweenPresents,FrameType" }.Concat(rows)); return file;
    }
    [Fact] public void CsvSelectsDominantChainFiltersPidAndGeneratedFramesAndSkipsFirstInterval()
    {
        var rows = Enumerable.Range(0, 1001).Select(i => $"\"game, test.exe\",42,0x1,{(i*.01).ToString(System.Globalization.CultureInfo.InvariantCulture)},10,Application").ToList();
        rows.Add("other.exe,99,0x1,11,10,Application"); rows.Add("game.exe,42,0x1,11,1,Intel XeSS-FG");
        rows.Add("game.exe,42,0x2,12,10,Application"); rows.Add("game.exe,42,0x2,13,10,Application");
        var data = PresentMonCsv.Read(Csv(rows),42);
        Assert.Equal(1000,data.Frames.Count); Assert.Equal(1,data.OtherSwapChains); Assert.Equal(100,data.Statistics!.AverageFps);
        Assert.Contains("Intel XeSS-FG", data.FrameTypes);
    }
    [Fact] public void InvalidRowsSuppressStatisticsAndRawFileSurvives()
    {
        var file = Csv(["game,42,1,0,10,Application", "game,42,1,1,NaN,Application"]);
        var data = PresentMonCsv.Read(file,42); Assert.Equal(1,data.InvalidRows); Assert.Null(data.Statistics); Assert.True(File.Exists(file));
    }
    [Fact] public void ParserRejectsUnknownSchemaAndHonorsCancellation()
    {
        var path = Path.Combine(directory,"bad.csv"); File.WriteAllText(path,"wrong,headers\n1,2");
        Assert.Throws<InvalidDataException>(() => PresentMonCsv.Read(path,42));
        var valid = Csv(["game,42,1,0,10,Application"]); using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PresentMonCsv.Read(valid,42,cts.Token));
    }
    [Fact] public async Task SqliteMigratesSavesAndRestoresAfterReopen()
    {
        var path = Path.Combine(directory,"sessions.db"); var session = Session();
        await new SqliteBenchmarkRepository(path).SaveAsync(session);
        var restored = Assert.Single(await new SqliteBenchmarkRepository(path).ListAsync());
        Assert.Equal(session.Id,restored.Id); Assert.Equal(session.Data!.Statistics,restored.Data!.Statistics);
        Assert.True(restored.Data.Frames.Count <= 600); Assert.Equal(session.RawSha256,restored.RawSha256);
        using var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); using var cmd=db.CreateCommand();
        cmd.CommandText="PRAGMA user_version"; Assert.Equal(1L,cmd.ExecuteScalar());
    }
    [Fact] public async Task DuplicateSaveRollsBackAndFutureSchemaIsNotModified()
    {
        var path=Path.Combine(directory,"sessions.db"); var repo=new SqliteBenchmarkRepository(path); var session=Session();
        await repo.SaveAsync(session); await Assert.ThrowsAsync<SqliteException>(()=>repo.SaveAsync(session));
        Assert.Single(await repo.ListAsync());
        using(var db=new SqliteConnection($"Data Source={path};Pooling=False")) { db.Open(); using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA user_version=99";cmd.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<InvalidDataException>(()=>repo.ListAsync());
    }
    [Fact] public async Task RepositoryCancellationDoesNotInsert()
    {
        var repo=new SqliteBenchmarkRepository(Path.Combine(directory,"sessions.db")); using var cts=new CancellationTokenSource();cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>repo.SaveAsync(Session(),cts.Token)); Assert.Empty(await repo.ListAsync());
    }
    public void Dispose() => Directory.Delete(directory,true);
}
