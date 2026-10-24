using System.Text.Json;
using Lotus.Core.Optimization;
using Microsoft.Data.Sqlite;

namespace Lotus.Infrastructure.Optimization;

// Dedicated test database; never reads or writes Windows/user preferences.
public sealed class DisposableOptimizationStore(string path) : IOptimizationJournal, IOptimizationAction
{
    public ActionDefinition Definition => new("lotus.test.preference.v1", "OptTestDescription", ActionRisk.Low, "Lotus disposable SQLite configuration v1", true);
    public Eligibility Eligibility => Eligibility.Eligible;
    public IDisposable Acquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA synchronous=FULL;"; cmd.ExecuteNonQuery();
            using var tx = db.BeginTransaction(); cmd.Transaction = tx;
            cmd.CommandText = "PRAGMA user_version"; var version = Convert.ToInt32(cmd.ExecuteScalar());
            if (version > 1) throw new InvalidDataException("Newer optimizer journal schema.");
            if (version == 0)
            {
                cmd.CommandText = "CREATE TABLE operations(id TEXT PRIMARY KEY, payload TEXT NOT NULL); CREATE TABLE test_setting(id INTEGER PRIMARY KEY CHECK(id=1), value TEXT, revision INTEGER NOT NULL, writer TEXT); INSERT INTO test_setting VALUES(1,NULL,0,NULL); PRAGMA user_version=1;";
                cmd.ExecuteNonQuery();
            }
            tx.Commit(); return db;
        }
        catch { db.Dispose(); throw; }
    }
    public void Save(OptimizationPlan plan)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO operations VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload";
        cmd.Parameters.AddWithValue("$id", plan.Id.ToString()); cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(plan));
        cmd.ExecuteNonQuery(); tx.Commit();
    }
    public OptimizationPlan? Find(Guid id) => List().FirstOrDefault(p => p.Id == id);
    public IReadOnlyList<OptimizationPlan> List()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT payload FROM operations ORDER BY rowid DESC";
        using var reader = cmd.ExecuteReader(); var plans = new List<OptimizationPlan>();
        while (reader.Read()) plans.Add(JsonSerializer.Deserialize<OptimizationPlan>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid journal."));
        return plans;
    }
    public ConfigurationState Detect()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT value,revision,writer FROM test_setting WHERE id=1";
        using var r = cmd.ExecuteReader(); if (!r.Read()) throw new InvalidDataException("Missing test setting.");
        return new(r.IsDBNull(0) ? null : r.GetString(0), r.GetInt64(1), r.IsDBNull(2) ? null : Guid.Parse(r.GetString(2)));
    }
    public ConfigurationState Plan(ConfigurationState before, Guid operation) => before.Value == "enabled" ? before : new("enabled", checked(before.Revision + 1), operation);
    public bool Exchange(ConfigurationState expected, ConfigurationState desired)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE test_setting SET value=$next,revision=$revision,writer=$writer WHERE id=1 AND value IS $before AND revision=$oldRevision AND writer IS $oldWriter";
        cmd.Parameters.AddWithValue("$next", (object?)desired.Value ?? DBNull.Value); cmd.Parameters.AddWithValue("$revision", desired.Revision);
        cmd.Parameters.AddWithValue("$writer", (object?)desired.Writer?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$before", (object?)expected.Value ?? DBNull.Value); cmd.Parameters.AddWithValue("$oldRevision", expected.Revision);
        cmd.Parameters.AddWithValue("$oldWriter", (object?)expected.Writer?.ToString() ?? DBNull.Value);
        var changed = cmd.ExecuteNonQuery() == 1; tx.Commit(); return changed;
    }
}
