using System.Text.Json;
using System.Text.Json.Serialization;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// %LOCALAPPDATA%\Lotus\player.json — favorites and notes. Written atomically (temp file + replace).
public sealed class JsonPlayerJournalStore(string? path = null) : IPlayerJournalStore
{
    private readonly string file = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "player.json");

    public PlayerJournalData Load()
    {
        if (!File.Exists(file)) return PlayerJournalData.Empty;
        var dto = JsonSerializer.Deserialize(File.ReadAllText(file), PlayerJson.Default.PlayerDto);
        return dto is null ? PlayerJournalData.Empty : new([.. dto.Favorites ?? []], new(dto.Notes ?? []));
    }

    public void Save(PlayerJournalData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new PlayerDto([.. data.Favorites.Order()], data.Notes), PlayerJson.Default.PlayerDto));
        File.Move(tmp, file, overwrite: true);
    }
}

public sealed record PlayerDto(List<string>? Favorites, Dictionary<string, string>? Notes);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PlayerDto))]
internal sealed partial class PlayerJson : JsonSerializerContext { }
