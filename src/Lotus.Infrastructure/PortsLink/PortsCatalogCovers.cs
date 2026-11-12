using System.Text.Json;
using Microsoft.Win32;

namespace Lotus.Infrastructure.PortsLink;

// Covers of the Lotus Ports games while Lotus Ports is closed: read from the catalog it ships next to its executable
// (catalog\games\<id>.json → lotusGameKey + cover.url). Read-only; a missing or changed format just gives nothing.
public static class PortsCatalogCovers
{
    public static IReadOnlyDictionary<string, string> Read(string? portsExe = null)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Folders(portsExe ?? RegisteredExe()))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.json").Take(2000).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
                if (Parse(SafeRead(file)) is { } hit) found.TryAdd(hit.Key, hit.Cover);
        }
        return found;
    }

    public static (string Key, string Cover)? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var key = root.TryGetProperty("lotusGameKey", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            string? cover = null;
            if (root.TryGetProperty("cover", out var c))
                cover = c.ValueKind == JsonValueKind.String ? c.GetString()
                    : c.ValueKind == JsonValueKind.Object && c.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            return key is { Length: > 0 } && cover is { Length: > 0 } ? (key, cover) : null;
        }
        catch (JsonException) { return null; }
    }

    private static IEnumerable<string> Folders(string? exe)
    {
        var list = new List<string>();
        if (exe is { Length: > 3 } && Path.GetDirectoryName(exe) is { } dir) list.Add(Path.Combine(dir, "catalog", "games"));
        list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LotusPorts", "catalog", "games"));
        return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? RegisteredExe()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey(Lotus.Integration.Contracts.Protocol.PortsRegistryKey);
        return key?.GetValue(Lotus.Integration.Contracts.RegistryValueNames.ExePath) as string;
    }

    private static string? SafeRead(string file)
    {
        try { return new FileInfo(file).Length < 512 * 1024 ? File.ReadAllText(file) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
