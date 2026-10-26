using System.Text.Json;
using System.Text.Json.Serialization;
using Lotus.Core.Diary;

namespace Lotus.Infrastructure.Games;

// %LOCALAPPDATA%\Lotus\diary.json — the player's game diary. Written atomically; a previous copy is kept as .bak.
public sealed class JsonGameDiaryStore(string? path = null) : IGameDiaryStore
{
    private readonly string file = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "diary.json");

    public DiaryData Load()
    {
        foreach (var f in new[] { file, file + ".bak" })
        {
            if (!File.Exists(f)) continue;
            try { if (JsonSerializer.Deserialize(File.ReadAllText(f), DiaryJson.Default.DiaryData) is { } d) return d; }
            catch (JsonException) { } // damaged: try the backup
        }
        return new();
    }

    public void Save(DiaryData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, DiaryJson.Default.DiaryData));
        if (File.Exists(file)) File.Copy(file, file + ".bak", overwrite: true);
        File.Move(tmp, file, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiaryData))]
internal sealed partial class DiaryJson : JsonSerializerContext { }
