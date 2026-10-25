using System.Text.Json;
using Lotus.Core.Optimization;
using Microsoft.Data.Sqlite;

namespace Lotus.Infrastructure.Optimization;

public class SqliteOptimizationJournal(string path) : IOptimizationJournal, IProfileStore
{
    public IDisposable Acquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    protected SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA synchronous=FULL;"; cmd.ExecuteNonQuery();
            using var tx = db.BeginTransaction(); cmd.Transaction = tx;
            cmd.CommandText = "PRAGMA user_version"; var version = Convert.ToInt32(cmd.ExecuteScalar());
            if (version > 2) throw new InvalidDataException("Newer optimizer journal schema.");
            if (version == 0)
            {
                cmd.CommandText = "CREATE TABLE operations(id TEXT PRIMARY KEY, payload TEXT NOT NULL); CREATE TABLE test_setting(id INTEGER PRIMARY KEY CHECK(id=1), value TEXT, revision INTEGER NOT NULL, writer TEXT); INSERT INTO test_setting VALUES(1,NULL,0,NULL); PRAGMA user_version=1;";
                cmd.ExecuteNonQuery();
            }
            if (version < 2)
            {
                cmd.CommandText = "CREATE TABLE profiles(id TEXT PRIMARY KEY, payload TEXT NOT NULL); PRAGMA user_version=2;";
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
    public IReadOnlyList<OptimizationProfile> LoadProfiles()
    {
        using var lease = Acquire(); using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM profiles"; using var reader = cmd.ExecuteReader();
        var profiles = new List<OptimizationProfile>();
        while(reader.Read()) profiles.Add(JsonSerializer.Deserialize<OptimizationProfile>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid profile."));
        return profiles;
    }
    public void SaveProfile(OptimizationProfile profile)
    {
        if (profile.Id is not ("game" or "daily" or "custom")) throw new ArgumentException("Unknown profile.");
        using var lease = Acquire(); using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO profiles VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload";
        cmd.Parameters.AddWithValue("$id", profile.Id); cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(profile));
        cmd.ExecuteNonQuery(); tx.Commit();
    }
}
