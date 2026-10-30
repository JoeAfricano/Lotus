using System.Text.Json;
using System.Text.Json.Serialization;
using Lotus.Core.Diary;

namespace Lotus.Infrastructure.Games;

// %LOCALAPPDATA%\Lotus\resume-notes.json — "Onde eu parei?" notes. Written atomically (temp file, then replace with a
// .bak of the previous version); a damaged file falls back to the .bak instead of losing every note.
public sealed class JsonResumeNoteStore(string? path = null) : IResumeNoteStore
{
    private readonly string file = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "resume-notes.json");
    public string FilePath => file;

    public ResumeNoteData Load()
    {
        foreach (var candidate in new[] { file, file + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try { if (JsonSerializer.Deserialize(File.ReadAllText(candidate), ResumeJson.Default.ResumeNoteData) is { } d) return d; }
            catch (JsonException) { } // try the backup
        }
        return new ResumeNoteData();
    }

    public void Save(ResumeNoteData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, ResumeJson.Default.ResumeNoteData));
        if (File.Exists(file)) File.Replace(tmp, file, file + ".bak");
        else File.Move(tmp, file);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ResumeNoteData))]
internal sealed partial class ResumeJson : JsonSerializerContext { }
