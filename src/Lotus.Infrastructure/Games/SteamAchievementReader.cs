using System.Text;
using Lotus.Core.Achievements;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Games;

// Steam keeps every played game's achievements on this PC (appcache\stats): the schema (names in each language,
// icons, hidden flag) and the account's unlocked bits with unlock times. Reading them needs no login, no API key and
// works with a private profile. Read-only.
public sealed class SteamAchievementReader(string language = "pt-BR", string? steamPath = null) : ISteamAchievementSource
{
    private readonly Dictionary<string, (DateTime Stamp, Schema? Schema)> schemas = [];

    public IReadOnlyList<SteamGameAchievements> Read()
    {
        try
        {
            var steam = steamPath ?? (!OperatingSystem.IsWindows() ? null : Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string)?.Replace('/', Path.DirectorySeparatorChar);
            if (steam is null) return [];
            var stats = Path.Combine(steam, "appcache", "stats");
            var account = Account(steam);
            if (account is null || !Directory.Exists(stats)) return [];
            var games = new List<SteamGameAchievements>();
            foreach (var userFile in Directory.EnumerateFiles(stats, $"UserGameStats_{account}_*.bin"))
            {
                var appId = Path.GetFileNameWithoutExtension(userFile)[$"UserGameStats_{account}_".Length..];
                var schemaFile = Path.Combine(stats, $"UserGameStatsSchema_{appId}.bin");
                if (!appId.All(char.IsDigit) || !File.Exists(schemaFile)) continue;
                try { if (Game(appId, schemaFile, userFile) is { Total: > 0 } g) games.Add(g); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException or ArgumentException) { }
            }
            return games;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    // The most recently used account: its userdata folder is named after the account id used in the stats files.
    private static string? Account(string steam)
    {
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) return null;
        return Directory.EnumerateDirectories(userdata).Where(d => Path.GetFileName(d).All(char.IsDigit) && Path.GetFileName(d) != "0")
            .OrderByDescending(d => File.Exists(Path.Combine(d, "config", "localconfig.vdf")) ? File.GetLastWriteTimeUtc(Path.Combine(d, "config", "localconfig.vdf")) : DateTime.MinValue)
            .Select(Path.GetFileName).FirstOrDefault();
    }

    private SteamGameAchievements? Game(string appId, string schemaFile, string userFile)
    {
        var stamp = File.GetLastWriteTimeUtc(schemaFile);
        if (!schemas.TryGetValue(schemaFile, out var cached) || cached.Stamp != stamp)
            schemas[schemaFile] = cached = (stamp, ParseSchema(appId, BinaryKv.Parse(File.ReadAllBytes(schemaFile))));
        if (cached.Schema is not { } schema) return null;
        return Build(appId, schema, BinaryKv.Parse(File.ReadAllBytes(userFile)));
    }

    public sealed record SchemaItem(string Stat, int Bit, string ApiName, string Name, string Description, bool Hidden, string? Icon, string? IconGray);
    public sealed record Schema(string Name, IReadOnlyList<SchemaItem> Items);

    public Schema? ParseSchema(string appId, BinaryKv.Node root)
    {
        var app = root.Child(appId) ?? root.Children.Values.OfType<BinaryKv.Node>().FirstOrDefault();
        if (app?.Child("stats") is not { } stats) return null;
        var items = new List<SchemaItem>();
        foreach (var (statId, value) in stats.Children)
        {
            if (value is not BinaryKv.Node stat || stat.Child("bits") is not { } bits) continue;
            foreach (var (bitKey, bitValue) in bits.Children)
            {
                if (bitValue is not BinaryKv.Node bit || !int.TryParse(bitKey, out var index)) continue;
                var display = bit.Child("display");
                var api = bit.Text("name") ?? $"{statId}_{index}";
                items.Add(new(statId, bit.Int("bit") ?? index, api, Localized(display, "name") ?? api, Localized(display, "desc") ?? "",
                    (display?.Int("hidden") ?? (int.TryParse(display?.Text("hidden"), out var h) ? h : 0)) != 0, display?.Text("icon"), display?.Text("icon_gray")));
            }
        }
        return new(app.Text("gamename") ?? appId, items);
    }

    public static SteamGameAchievements Build(string appId, Schema schema, BinaryKv.Node user)
    {
        var cache = user.Child("cache") ?? user;
        var items = schema.Items.Select(i =>
        {
            var stat = cache.Child(i.Stat);
            var data = stat?.Int("data") ?? 0;
            var unlocked = i.Bit is >= 0 and < 32 && ((data >> i.Bit) & 1) == 1;
            var time = stat?.Child("AchievementTimes")?.Int(i.Bit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new SteamAchievement(i.ApiName, i.Name, i.Description, i.Hidden, i.Icon, i.IconGray, unlocked,
                unlocked && time is > 0 ? DateTimeOffset.FromUnixTimeSeconds(time.Value) : null);
        }).ToList();
        return new(appId, schema.Name, items);
    }

    // Name/description in the player's language: Brazilian Portuguese falls back to Portuguese, then English.
    private string? Localized(BinaryKv.Node? display, string key)
    {
        if (display is null) return null;
        if (display.Text(key) is { } plain) return plain;
        if (display.Child(key) is not { } langs) return null;
        var order = language.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? new[] { "brazilian", "portuguese", "english" } : ["english"];
        foreach (var l in order) if (langs.Text(l) is { Length: > 0 } s) return s;
        return langs.Children.Values.OfType<string>().FirstOrDefault(s => s.Length > 0);
    }
}

// Valve's binary KeyValues: type byte, null-terminated name, value; 0x08 closes a section.
public static class BinaryKv
{
    public sealed class Node
    {
        public Dictionary<string, object> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Node? Child(string name) => Children.GetValueOrDefault(name) as Node;
        public string? Text(string name) => Children.GetValueOrDefault(name) as string;
        public int? Int(string name) => Children.GetValueOrDefault(name) switch { int i => i, ulong u => (int)u, string s when int.TryParse(s, out var v) => v, _ => null };
    }

    public static Node Parse(byte[] bytes)
    {
        var pos = 0;
        var root = new Node();
        Read(root);
        return root;

        void Read(Node node)
        {
            while (pos < bytes.Length)
            {
                var type = bytes[pos++];
                if (type is 8 or 11) return;
                var name = Str();
                switch (type)
                {
                    case 0: var child = new Node(); Read(child); node.Children[name] = child; break;
                    case 1: node.Children[name] = Str(); break;
                    case 2: node.Children[name] = BitConverter.ToInt32(bytes, pos); pos += 4; break;
                    case 3: node.Children[name] = BitConverter.ToSingle(bytes, pos).ToString(System.Globalization.CultureInfo.InvariantCulture); pos += 4; break;
                    case 4: case 6: pos += 4; break; // pointer / colour: not used here
                    case 7: case 10: node.Children[name] = BitConverter.ToUInt64(bytes, pos); pos += 8; break;
                    default: throw new FormatException($"Unknown KeyValues type {type}");
                }
            }
        }
        string Str()
        {
            var start = pos;
            while (pos < bytes.Length && bytes[pos] != 0) pos++;
            var s = Encoding.UTF8.GetString(bytes, start, pos - start);
            pos++;
            return s;
        }
    }
}
