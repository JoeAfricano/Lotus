using System.Text.Json;
using System.Text.Json.Serialization;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// %LOCALAPPDATA%\Lotus\game-prefs.json — per-game "Preparar e Jogar" choices and pending restores. Atomic writes with a
// .bak of the previous version.
public sealed class JsonGamePrefsStore(string? path = null) : IGamePrefsStore
{
    private readonly string file = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "game-prefs.json");
    public GamePrefsData Load()
    {
        foreach (var candidate in new[] { file, file + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try { if (JsonSerializer.Deserialize(File.ReadAllText(candidate), PrefsJson.Default.GamePrefsData) is { } d) return d; }
            catch (JsonException) { }
        }
        return new GamePrefsData();
    }
    public void Save(GamePrefsData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, PrefsJson.Default.GamePrefsData));
        if (File.Exists(file)) File.Replace(tmp, file, file + ".bak"); else File.Move(tmp, file);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GamePrefsData))]
internal sealed partial class PrefsJson : JsonSerializerContext { }

// Read-only checks used before playing. The controller count comes from the app (Windows.Gaming.Input lives there).
public sealed class WindowsPrepareProbes(IMemoryPressure? memory, Func<int?>? controllers = null) : IPrepareProbes
{
    public bool InstallPresent(GameEntry game)
    {
        if (game.ExePath is { Length: > 0 } exe && !File.Exists(exe)) return false;
        if (game.InstallDir is { Length: > 0 } dir && !Directory.Exists(dir)) return false;
        return true; // store-only entries (launch URI) have nothing local to check
    }

    public long? FreeBytesFor(GameEntry game)
    {
        var path = game.InstallDir ?? game.ExePath;
        if (path is null || Path.GetPathRoot(path) is not { Length: > 0 } root) return null;
        try { var d = new DriveInfo(root); return d.IsReady ? d.AvailableFreeSpace : null; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    public (double LoadPercent, IReadOnlyList<string> TopApps)? Memory(GameEntry game) => memory?.Read(game);
    public int? Controllers() => controllers?.Invoke();
}
