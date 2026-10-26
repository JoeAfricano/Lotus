using System.Text.Json;
using System.Text.Json.Serialization;
using Lotus.Core.Achievements;

namespace Lotus.Infrastructure.Games;

// %LOCALAPPDATA%\Lotus\achievements.json — earned Lotus achievements, activity counters and Steam sync state.
// Written atomically; a previous copy is kept as .bak.
public sealed class JsonTrophyLedgerStore(string? path = null) : ITrophyLedgerStore
{
    private readonly string file = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "achievements.json");

    public TrophyLedger Load()
    {
        foreach (var f in new[] { file, file + ".bak" })
        {
            if (!File.Exists(f)) continue;
            try { if (JsonSerializer.Deserialize(File.ReadAllText(f), TrophyJson.Default.TrophyLedger) is { } d) return d; }
            catch (JsonException) { } // damaged: try the backup
        }
        return new();
    }

    public void Save(TrophyLedger ledger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ledger, TrophyJson.Default.TrophyLedger));
        if (File.Exists(file)) File.Copy(file, file + ".bak", overwrite: true);
        File.Move(tmp, file, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TrophyLedger))]
internal sealed partial class TrophyJson : JsonSerializerContext { }
