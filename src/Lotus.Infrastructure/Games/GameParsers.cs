using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Lotus.Infrastructure.Games;

// Minimal reader for Valve KeyValues text (libraryfolders.vdf, appmanifest_*.acf). Read-only.
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Get(string key) => Values.GetValueOrDefault(key);
    public VdfNode? Child(string key) => Children.GetValueOrDefault(key);

    public static VdfNode Parse(string text)
    {
        var root = new VdfNode(); var stack = new Stack<VdfNode>(); stack.Push(root);
        string? pendingKey = null; var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (c == '{') { var node = new VdfNode(); if (pendingKey is not null) stack.Peek().Children[pendingKey] = node; stack.Push(node); pendingKey = null; i++; continue; }
            if (c == '}') { if (stack.Count > 1) stack.Pop(); pendingKey = null; i++; continue; }
            if (c != '"') { i++; continue; }
            var sb = new StringBuilder(); i++;
            while (i < text.Length && text[i] != '"')
            {
                if (text[i] == '\\' && i + 1 < text.Length) { i++; sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] }); }
                else sb.Append(text[i]);
                i++;
            }
            i++;
            if (pendingKey is null) pendingKey = sb.ToString();
            else { stack.Peek().Values[pendingKey] = sb.ToString(); pendingKey = null; }
        }
        return root;
    }
}

public sealed record SteamApp(string AppId, string Name, string InstallDir);
public sealed record EpicItem(string AppName, string Name, string InstallLocation, string? LaunchExecutable, string? LaunchUri);
public sealed record XboxGame(string IdentityName, string Name, string? PackageFamilyName, string? AppId, string? Executable, string? Logo);
public sealed record EaGame(string Name, IReadOnlyList<string> ContentIds);

public static class GameParsers
{
    // --- Steam ---
    public static IReadOnlyList<string> SteamLibraries(string libraryFoldersVdf) =>
        VdfNode.Parse(libraryFoldersVdf).Child("libraryfolders")?.Children.Values
            .Select(c => c.Get("path")).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Replace("\\\\", "\\").Replace('/', '\\')).ToList() ?? [];

    private static readonly string[] SteamTools = ["Steamworks Common", "Proton", "Steam Linux Runtime", "SteamVR"];
    public static SteamApp? SteamManifest(string acf)
    {
        var state = VdfNode.Parse(acf).Child("AppState");
        string? id = state?.Get("appid"), name = state?.Get("name"), dir = state?.Get("installdir");
        if (id is null || name is null || dir is null) return null;
        if (long.TryParse(state!.Get("StateFlags"), out var flags) && (flags & 4) == 0) return null; // 4 = fully installed
        if (id == "228980" || SteamTools.Any(t => name.StartsWith(t, StringComparison.OrdinalIgnoreCase))) return null;
        return new(id, name, dir);
    }

    // --- Epic ---
    public static EpicItem? EpicManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        string? name = S("DisplayName"), location = S("InstallLocation"), app = S("AppName"), ns = S("CatalogNamespace"), item = S("CatalogItemId");
        if (name is null || location is null || app is null) return null;
        // Engines and tools are listed with categories that do not include "games".
        if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array && cats.GetArrayLength() > 0
            && !cats.EnumerateArray().Any(c => string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase))) return null;
        var uri = ns is not null && item is not null ? $"com.epicgames.launcher://apps/{ns}%3A{item}%3A{app}?action=launch&silent=true" : null;
        return new(app, name, location, S("LaunchExecutable"), uri);
    }

    // --- Xbox / Game Pass (GDK) ---
    // .GamingRoot: "RGBX" + uint32 count + UTF-16 null-terminated folder names relative to the drive root.
    public static IReadOnlyList<string> GamingRootFolders(ReadOnlySpan<byte> data)
    {
        var list = new List<string>();
        if (data.Length < 8 || data[0] != (byte)'R' || data[1] != (byte)'G' || data[2] != (byte)'B' || data[3] != (byte)'X') return list;
        var text = Encoding.Unicode.GetString(data[8..]);
        foreach (var part in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            if (part.Trim().Length > 0 && part.IndexOfAny(Path.GetInvalidPathChars()) < 0) list.Add(part.Trim().TrimStart('\\'));
        return list;
    }

    public static XboxGame? MicrosoftGameConfig(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root; if (root is null) return null;
        XElement? E(string n) => root.Descendants().FirstOrDefault(e => e.Name.LocalName == n);
        var identity = E("Identity"); var visuals = E("ShellVisuals");
        var name = identity?.Attribute("Name")?.Value; if (string.IsNullOrWhiteSpace(name)) return null;
        var publisher = identity?.Attribute("Publisher")?.Value;
        var exe = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable");
        var display = visuals?.Attribute("DefaultDisplayName")?.Value;
        if (string.IsNullOrWhiteSpace(display) || display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) display = null;
        var logo = visuals?.Attribute("Square480x480Logo")?.Value ?? visuals?.Attribute("Square150x150Logo")?.Value ?? visuals?.Attribute("StoreLogo")?.Value;
        return new(name, display ?? name, publisher is null ? null : PackageFamilyName(name, publisher), exe?.Attribute("Id")?.Value, exe?.Attribute("Name")?.Value, logo);
    }

    // Windows package family name: Name + "_" + Crockford base32 of the first 8 bytes of SHA-256(UTF-16LE publisher).
    public static string PackageFamilyName(string name, string publisher)
    {
        const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        ulong bits = 0; for (var i = 0; i < 8; i++) bits = (bits << 8) | hash[i];
        var sb = new StringBuilder(13);
        for (var i = 0; i < 13; i++) { var shift = 64 - 5 * (i + 1); var v = shift >= 0 ? (int)((bits >> shift) & 31) : (int)((bits << -shift) & 31); sb.Append(Alphabet[v]); }
        return name + "_" + sb;
    }

    // --- EA app ---
    public static EaGame? EaInstallerData(string xml)
    {
        var root = XDocument.Parse(xml).Root; if (root is null) return null;
        var ids = root.Descendants().Where(e => e.Name.LocalName == "contentID").Select(e => e.Value.Trim()).Where(v => v.Length > 0).Distinct().ToList();
        var titles = root.Descendants().Where(e => e.Name.LocalName == "gameTitle").ToList();
        var title = titles.FirstOrDefault(t => (string?)t.Attribute("locale") is "pt_BR")?.Value
            ?? titles.FirstOrDefault(t => (string?)t.Attribute("locale") is "en_US")?.Value ?? titles.FirstOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(title) ? null : new(title.Trim(), ids);
    }
}
