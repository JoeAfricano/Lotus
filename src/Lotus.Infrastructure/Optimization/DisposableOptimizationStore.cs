using System.Text.Json;
using Lotus.Core.Optimization;
using Microsoft.Data.Sqlite;

namespace Lotus.Infrastructure.Optimization;

// Dedicated test database; never reads or writes Windows/user preferences.
public sealed class DisposableOptimizationStore(string path) : SqliteOptimizationJournal(path), IOptimizationAction
{
    public ActionDefinition Definition => new("lotus.test.preference.v1", "OptTestDescription", ActionRisk.Low, "Lotus disposable SQLite configuration v1", true);
    public Eligibility Eligibility => Eligibility.Eligible;
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
