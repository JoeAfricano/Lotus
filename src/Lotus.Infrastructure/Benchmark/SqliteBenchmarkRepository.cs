using System.Text.Json;
using Lotus.Core.Benchmark;
using Microsoft.Data.Sqlite;

namespace Lotus.Infrastructure.Benchmark;

// One transaction per completed capture. Frame data stays in hashed CSV; JSON contains bounded chart previews.
public sealed class SqliteBenchmarkRepository(string path) : IBenchmarkRepository
{
    private readonly SemaphoreSlim gate = new(1);
    public Task SaveAsync(BenchmarkSession session, CancellationToken token = default) => Task.Run(async () =>
    {
        await gate.WaitAsync(token);
        try
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO sessions(id,started_utc,payload) VALUES($id,$time,$payload)";
            cmd.Parameters.AddWithValue("$id", session.Id.ToString());
            cmd.Parameters.AddWithValue("$time", session.StartedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(Preview(session)));
            token.ThrowIfCancellationRequested(); cmd.ExecuteNonQuery(); tx.Commit();
        }
        finally { gate.Release(); }
    }, token);
    public Task<IReadOnlyList<BenchmarkSession>> ListAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<BenchmarkSession>>(async () =>
    {
        await gate.WaitAsync(token);
        try
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT payload FROM sessions ORDER BY started_utc DESC LIMIT 100";
            using var reader = cmd.ExecuteReader(); var sessions = new List<BenchmarkSession>();
            while (reader.Read()) { token.ThrowIfCancellationRequested(); sessions.Add(JsonSerializer.Deserialize<BenchmarkSession>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid session.")); }
            return sessions;
        }
        finally { gate.Release(); }
    }, token);
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 5 }.ToString());
        try
        {
            db.Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "PRAGMA user_version"; var version = Convert.ToInt32(cmd.ExecuteScalar());
            if (version > 1) throw new InvalidDataException("Database schema is newer than Lotus.");
            if (version == 0)
            {
                cmd.CommandText = "CREATE TABLE sessions(id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, payload TEXT NOT NULL); PRAGMA user_version=1;";
                cmd.ExecuteNonQuery();
            }
            tx.Commit(); return db;
        }
        catch { db.Dispose(); throw; }
    }
    public static BenchmarkSession Preview(BenchmarkSession session)
    {
        if (session.Data is not { } data || data.Frames.Count <= 600) return session;
        var width = (int)Math.Ceiling(data.Frames.Count / 600d);
        // Peak envelope preserves spikes. Raw CSV retains all frames; do not recalculate metrics from this preview.
        var points = data.Frames.Chunk(width).Select(bucket => bucket.MaxBy(p => p.Milliseconds)!).ToArray();
        return session with { Data = data with { Frames = points } };
    }
}
