using System.Text.Json;
using Microsoft.Win32;

namespace Lotus.Infrastructure.PortsLink;

// Covers of the Lotus Ports games while Lotus Ports is closed, from the catalog it ships: the "catalog.json" resource
// inside LotusPorts.Catalog.dll next to its executable (read as data with System.Reflection.Metadata; nothing is loaded
// or run), or loose catalog\games\<id>.json files. Each game gives lotusGameKey (or "port:" + id) and cover.url.
// Read-only; a missing or changed format just gives nothing.
public static class PortsCatalogCovers
{
    public static IReadOnlyDictionary<string, string> Read(string? portsExe = null)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exe = portsExe ?? RegisteredExe();
        if (exe is { Length: > 3 } && Path.GetDirectoryName(exe) is { } appDir)
        {
            var dll = Path.Combine(appDir, "LotusPorts.Catalog.dll");
            if (File.Exists(dll) && ResourceText(dll, "catalog.json") is { } catalog)
                foreach (var (key, cover) in ParseAll(catalog)) found.TryAdd(key, cover);
        }
        foreach (var dir in Folders(exe))
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
            return Game(doc.RootElement, requireKey: true);
        }
        catch (JsonException) { return null; }
    }

    // A whole catalog: every object that looks like a game (id or lotusGameKey, plus a cover), at any depth.
    public static List<(string Key, string Cover)> ParseAll(string json)
    {
        var list = new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
            Walk(doc.RootElement, list, 0);
        }
        catch (JsonException) { }
        return list;
    }

    private static void Walk(JsonElement e, List<(string, string)> list, int depth)
    {
        if (depth > 6) return;
        if (e.ValueKind == JsonValueKind.Array) { foreach (var item in e.EnumerateArray()) Walk(item, list, depth + 1); return; }
        if (e.ValueKind != JsonValueKind.Object) return;
        if (Game(e, requireKey: false) is { } hit) { list.Add(hit); return; }
        foreach (var p in e.EnumerateObject()) Walk(p.Value, list, depth + 1);
    }

    private static (string Key, string Cover)? Game(JsonElement root, bool requireKey)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var key = Str("lotusGameKey");
        if (key is not { Length: > 0 } && !requireKey && Str("id") is { Length: > 0 } id) key = "port:" + id;
        string? cover = null;
        if (root.TryGetProperty("cover", out var c))
            cover = c.ValueKind == JsonValueKind.String ? c.GetString()
                : c.ValueKind == JsonValueKind.Object && c.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        return key is { Length: > 0 } && cover is { Length: > 0 } ? (key, cover) : null;
    }

    // An embedded resource of a .NET assembly, read straight from the file (the assembly is never loaded).
    public static string? ResourceText(string assemblyPath, string resourceName)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is not { } cor) return null;
            var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            foreach (var handle in md.ManifestResources)
            {
                var res = md.GetManifestResource(handle);
                if (!res.Implementation.IsNil || !md.StringComparer.Equals(res.Name, resourceName)) continue;
                var section = pe.GetSectionData(cor.ResourcesDirectory.RelativeVirtualAddress);
                var reader = section.GetReader((int)res.Offset, section.Length - (int)res.Offset);
                var length = reader.ReadInt32();
                if (length <= 0 || length > 64 * 1024 * 1024 || length > reader.RemainingBytes) return null;
                var bytes = reader.ReadBytes(length);
                return System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException) { return null; }
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
