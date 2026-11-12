using Lotus.Core.Diary;
using Lotus.Core.Games;

namespace Lotus.Core.Saves;

// Where thousands of PC games keep their saves, from the Ludusavi manifest (github.com/mtkennerly/ludusavi-manifest,
// MIT, rebuilt daily from PCGamingWiki). Lotus keeps only what it can use safely: Windows save folders, written with
// the same tokens as SaveCatalog ({APPDATA}, {LOCALLOW}, {INSTALL}…). Matched by the store's own id (Steam, GOG);
// games without one (Epic, added by hand) only by their exact title, and those are marked so the player checks first.
public sealed record ManifestFolder(string Template, string? Store = null);
public sealed record ManifestGame(string Name, IReadOnlyList<string> Steam, IReadOnlyList<string> Gog, IReadOnlyList<ManifestFolder> Folders);
public sealed record ManifestMatch(IReadOnlyList<string> Templates, bool ByName);

public sealed class SaveManifest
{
    public const string Source = "manifest", NameSource = "manifest-name";
    private readonly Dictionary<string, ManifestGame> steam = new(StringComparer.Ordinal), gog = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManifestGame?> names = new(StringComparer.Ordinal); // null = two games share the title: never guessed

    public SaveManifest(IReadOnlyList<ManifestGame> games)
    {
        Games = games;
        foreach (var g in games)
        {
            foreach (var id in g.Steam) steam.TryAdd(id, g);
            foreach (var id in g.Gog) gog.TryAdd(id, g);
            var key = DiaryArt.Normalize(g.Name);
            if (key.Length >= 3) names[key] = names.ContainsKey(key) ? null : g;
        }
    }
    public IReadOnlyList<ManifestGame> Games { get; }

    public ManifestMatch? Find(GameEntry game)
    {
        var steamId = game.Store == GameStores.Steam ? game.Id
            : game.Store == GameStores.Manual && game.Id.StartsWith("steam-", StringComparison.Ordinal) ? game.Id[6..] : null;
        ManifestGame? hit = null; var byName = false;
        if (steamId is not null) steam.TryGetValue(steamId, out hit);
        else if (game.Store == GameStores.Gog) gog.TryGetValue(game.Id, out hit);
        if (hit is null && steamId is null && game.Store != GameStores.Gog && names.GetValueOrDefault(DiaryArt.Normalize(game.Name)) is { } named) { hit = named; byName = true; }
        if (hit is null) return null;
        var store = StoreName(game.Store);
        var templates = hit.Folders.Where(f => f.Store is null || f.Store == store).Select(f => f.Template).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return templates.Count > 0 ? new(templates, byName) : null;
    }

    // Lotus store → the manifest's "when: store:" names.
    public static string? StoreName(string store) => store switch
    {
        GameStores.Steam => "steam", GameStores.Gog => "gog", GameStores.Epic => "epic", GameStores.Xbox => "microsoft",
        GameStores.Ea => "ea", GameStores.Ubisoft => "uplay", _ => "other",
    };
}

// The manifest is YAML written by a serializer (two-space indents, one key per line). A small reader for exactly that
// shape keeps Lotus free of a YAML dependency; anything it does not understand is skipped, never guessed.
public static class LudusaviManifest
{
    private static readonly Dictionary<string, string> Bases = new(StringComparer.Ordinal)
    {
        ["<base>"] = "{INSTALL}", ["<home>"] = "{USERPROFILE}", ["<winAppData>"] = "{APPDATA}", ["<winLocalAppData>"] = "{LOCALAPPDATA}",
        ["<winLocalAppDataLow>"] = "{LOCALLOW}", ["<winDocuments>"] = "{DOCUMENTS}", ["<winPublic>"] = "{PUBLIC}", ["<winProgramData>"] = "{PROGRAMDATA}",
    };
    // <home>\… spelled out → the known folder itself (it can be moved, and the generic-folder check needs it).
    private static readonly (string[] Parts, string Token)[] HomeFolders =
    [
        (["AppData", "LocalLow"], "{LOCALLOW}"), (["AppData", "Roaming"], "{APPDATA}"), (["AppData", "Local"], "{LOCALAPPDATA}"),
        (["Saved Games"], "{SAVEDGAMES}"), (["Documents"], "{DOCUMENTS}"),
    ];
    // Shared folders that are never "the save" by themselves.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "My Games", "Saved Games", "Games", "Documents", "AppData", "Local", "LocalLow", "Roaming", "Microsoft", "Packages", "Temp", "Programs",
        "Steam", "Epic", "Unity", "UnrealEngine", "Ubisoft", "EA", "Electronic Arts", "GOG.com", "Rockstar Games", "SEGA", "Bandai Namco", "Saves", "Save",
        "SaveGames", "Saved", "Common Files", "Desktop", "Downloads", "OneDrive", "Public", "Users",
    };

    // A Ludusavi path → a Lotus folder template, or null when it is not a usable Windows folder.
    // "<home>/AppData/LocalLow/Team Cherry/Hollow Knight/*.dat" → "{LOCALLOW}\Team Cherry\Hollow Knight".
    public static string? ToTemplate(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count < 2 || !Bases.TryGetValue(parts[0], out var token)) return null;
        parts.RemoveAt(0);
        if (token == "{USERPROFILE}")
            foreach (var (home, known) in HomeFolders)
                if (parts.Count >= home.Length && home.Select((h, i) => string.Equals(h, parts[i], StringComparison.OrdinalIgnoreCase)).All(x => x)) { token = known; parts.RemoveRange(0, home.Length); break; }
        var literal = new List<string>();
        var cut = false;
        foreach (var p in parts)
        {
            if (p.IndexOfAny(['*', '?', '[', ']', '{', '}', '<', '>']) >= 0 || p is "." or "..") { cut = true; break; }
            literal.Add(p);
        }
        // Without a wildcard the last part may be a file: kept as is (SaveDiscovery uses its folder when it is a file).
        if (literal.Count == 0) return null;
        // One folder under a shared base must be the game's own; under the game's install folder anything but a bare file is.
        if (literal.Count == 1 && (token == "{INSTALL}" ? !cut && literal[0].Contains('.') : Generic.Contains(literal[0]))) return null;
        if (token == "{USERPROFILE}" && literal.Count < 2) return null; // straight in the user folder: too broad
        return token + "\\" + string.Join('\\', literal);
    }

    public static IReadOnlyList<ManifestGame> Parse(TextReader reader)
    {
        var games = new List<ManifestGame>();
        string? name = null; var steamIds = new List<string>(); var gogIds = new List<string>();
        var folders = new List<ManifestFolder>();
        string? section = null, sub = null; // top key under the game (files, steam, gog, id…) and the key under it
        string? file = null; var tags = new List<string>(); var whens = new List<(string? Os, string? Store)>(); string? fileKey = null;

        void EndFile()
        {
            if (file is null) return;
            var save = tags.Count == 0 || tags.Contains("save");
            var windows = whens.Count == 0 ? [(null, null)] : whens.Where(w => w.Os is null or "windows").ToList();
            if (save && windows.Count > 0 && ToTemplate(file) is { } template)
            {
                var stores = windows.Select(w => w.Store).Distinct().ToList();
                foreach (var s in stores.Contains(null) ? [null] : stores) folders.Add(new(template, s));
            }
            file = null; tags.Clear(); whens.Clear(); fileKey = null;
        }
        void EndGame()
        {
            EndFile();
            if (name is not null && folders.Count > 0)
            {
                // Same folder from several files once; a folder inside another one of the same store is already covered.
                var unique = folders.DistinctBy(f => (f.Template.ToLowerInvariant(), f.Store)).ToList();
                var kept = unique.Where(f => !unique.Any(o => o != f && o.Store == f.Store && f.Template.StartsWith(o.Template + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
                games.Add(new(name, [.. steamIds.Distinct()], [.. gogIds.Distinct()], kept));
            }
            name = null; steamIds.Clear(); gogIds.Clear(); folders.Clear(); section = sub = null;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var indent = line.Length - line.TrimStart(' ').Length;
            var text = line.Trim();
            if (indent == 0)
            {
                EndGame();
                if (KeyOf(text, out var k, out _)) name = k;
                continue;
            }
            if (name is null) continue;
            if (indent == 2)
            {
                EndFile();
                if (KeyOf(text, out var k, out _)) { section = k; sub = null; }
                continue;
            }
            switch (section)
            {
                case "steam" or "gog" when indent == 4 && KeyOf(text, out var k, out var v) && k == "id" && IsId(v):
                    (section == "steam" ? steamIds : gogIds).Add(v!); break;
                case "id" when indent == 4 && KeyOf(text, out var k, out _):
                    sub = k; break;
                case "id" when indent == 6 && text.StartsWith("- ") && IsId(text[2..].Trim()):
                    if (sub == "steamExtra") steamIds.Add(text[2..].Trim()); else if (sub == "gogExtra") gogIds.Add(text[2..].Trim());
                    break;
                case "files" when indent == 4 && KeyOf(text, out var k, out _):
                    EndFile(); file = k; break;
                case "files" when indent == 6 && file is not null && KeyOf(text, out var k, out _):
                    fileKey = k; break;
                case "files" when indent == 8 && file is not null && text.StartsWith("- "):
                    var item = text[2..].Trim();
                    if (fileKey == "tags") tags.Add(item);
                    else if (fileKey == "when") whens.Add(KeyOf(item, out var wk, out var wv) ? (wk == "os" ? wv : null, wk == "store" ? wv : null) : (null, null));
                    break;
                case "files" when indent == 10 && file is not null && fileKey == "when" && whens.Count > 0 && KeyOf(text, out var k, out var v):
                    // "- os: windows" followed by "  store: steam": the same condition
                    var last = whens[^1];
                    whens[^1] = k == "os" ? (v, last.Store) : k == "store" ? (last.Os, v) : last;
                    break;
            }
        }
        EndGame();
        return games;
    }

    private static bool IsId(string? v) => v is { Length: > 0 } && v.All(char.IsAsciiDigit);

    // `key: value`, `"quoted key": value` or `'quoted': value`; value may be empty.
    public static bool KeyOf(string text, out string key, out string? value)
    {
        key = ""; value = null;
        if (text.Length == 0) return false;
        int end;
        if (text[0] == '"')
        {
            var sb = new System.Text.StringBuilder();
            var i = 1;
            for (; i < text.Length && text[i] != '"'; i++)
            {
                if (text[i] != '\\' || i + 1 >= text.Length) { sb.Append(text[i]); continue; }
                var c = text[++i];
                if (c == 'u' && i + 4 < text.Length && int.TryParse(text.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code)) { sb.Append((char)code); i += 4; }
                else sb.Append(c switch { 'n' => '\n', 't' => '\t', '0' => '\0', _ => c });
            }
            if (i >= text.Length) return false;
            key = sb.ToString(); end = i + 1;
        }
        else if (text[0] == '\'')
        {
            var sb = new System.Text.StringBuilder();
            var i = 1;
            for (; i < text.Length; i++)
            {
                if (text[i] == '\'') { if (i + 1 < text.Length && text[i + 1] == '\'') { sb.Append('\''); i++; continue; } break; }
                sb.Append(text[i]);
            }
            if (i >= text.Length) return false;
            key = sb.ToString(); end = i + 1;
        }
        else
        {
            var colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0 && text.EndsWith(':')) colon = text.Length - 1;
            if (colon <= 0) return false;
            key = text[..colon]; end = colon;
        }
        if (end >= text.Length || text[end] != ':') return false;
        var rest = text[(end + 1)..].Trim();
        value = rest.Length == 0 ? null : rest.Trim('"', '\'');
        return true;
    }
}
