using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Manual games persisted as JSON; written to a temp file and moved so a crash never leaves a half-written list.
public sealed class JsonManualGameStore(string path) : IManualGameStore
{
    // ExePath is optional: a game added only by name (and cover) keeps an Id; Launch is a store link (steam://run/…).
    private sealed record Item(string? ExePath, string Name, string? Args, string? Id = null, string? Cover = null, string? Launch = null);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public IReadOnlyList<GameEntry> Load()
    {
        if (!File.Exists(path)) return [];
        var items = JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(path)) ?? [];
        var list = new List<GameEntry>();
        foreach (var i in items.Where(i => !string.IsNullOrWhiteSpace(i.Name) || !string.IsNullOrWhiteSpace(i.ExePath)))
        {
            if (i.ExePath is { Length: > 0 } exe && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                list.Add(new GameEntry(GameStores.Manual, exe.ToLowerInvariant(), i.Name, Path.GetDirectoryName(exe), exe, i.Launch, i.Args, i.Cover));
            else if (i.Id is { Length: > 0 } id)
                list.Add(new GameEntry(GameStores.Manual, id, i.Name, null, null, i.Launch, i.Args, i.Cover));
        }
        return list;
    }

    public void Save(IReadOnlyList<GameEntry> games) =>
        Write(path, games.Select(g => new Item(g.ExePath, g.Name, g.Args, g.ExePath is null ? g.Id : null, g.CoverPath, g.LaunchUri)).ToList());

    private string HiddenPath => Path.Combine(Path.GetDirectoryName(path)!, "games-hidden.json");
    public IReadOnlyList<string> LoadHidden() => File.Exists(HiddenPath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HiddenPath)) ?? [] : [];
    public void SaveHidden(IReadOnlyList<string> keys) => Write(HiddenPath, keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    // The player's own names and covers, by game key.
    private string CustomPath => Path.Combine(Path.GetDirectoryName(path)!, "games-custom.json");
    public IReadOnlyDictionary<string, GameCustom> LoadCustom()
    {
        try { return File.Exists(CustomPath) ? JsonSerializer.Deserialize<Dictionary<string, GameCustom>>(File.ReadAllText(CustomPath)) is { } d ? new Dictionary<string, GameCustom>(d, StringComparer.OrdinalIgnoreCase) : [] : []; }
        catch (JsonException) { return new Dictionary<string, GameCustom>(); }
    }
    public void SaveCustom(IReadOnlyDictionary<string, GameCustom> custom) => Write(CustomPath, custom.ToDictionary(kv => kv.Key, kv => kv.Value));

    // A cover picked by the player is copied into Lotus\Covers, so moving or deleting the original never loses it.
    public string? ImportCover(string gameKey, string sourceFile)
    {
        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp") || !File.Exists(sourceFile)) return null;
        var folder = Path.Combine(Path.GetDirectoryName(path)!, "Covers");
        Directory.CreateDirectory(folder);
        var name = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(gameKey.ToLowerInvariant())))[..16] + "-" + DateTime.UtcNow.Ticks + ext;
        var target = Path.Combine(folder, name);
        File.Copy(sourceFile, target, overwrite: true);
        // Older copies of this game's cover go away.
        foreach (var old in Directory.EnumerateFiles(folder, name[..16] + "-*").Where(f => !string.Equals(f, target, StringComparison.OrdinalIgnoreCase)))
            try { File.Delete(old); } catch (IOException) { }
        return target;
    }

    public void DeleteCover(string? coverPath)
    {
        if (coverPath is not { Length: > 0 }) return;
        var folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "Covers")) + Path.DirectorySeparatorChar;
        try { var full = Path.GetFullPath(coverPath); if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    private static void Write<T>(string target, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, target, overwrite: true);
    }
}
// Starts a game only through known launcher URI schemes, a Windows app (shell:AppsFolder) or an existing .exe.
[SupportedOSPlatform("windows")]
public sealed class WindowsGameLauncher : IGameLauncher
{
    private static readonly string[] Schemes = ["steam://", "com.epicgames.launcher://", "uplay://", "origin2://"];
    public static bool IsAllowedUri(string uri) =>
        Schemes.Any(s => uri.StartsWith(s, StringComparison.OrdinalIgnoreCase))
        || (uri.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) && uri.IndexOfAny(['"', '&', '|', '<', '>', '^']) < 0);

    public void Launch(GameEntry game)
    {
        if (game.LaunchUri is { } uri)
        {
            if (!IsAllowedUri(uri)) throw new InvalidOperationException("Launch URI scheme is not allowed.");
            // Windows apps, and any launcher link when Lotus runs as administrator, go through Explorer: it opens the
            // link as the signed-in user, so an already running Steam/Epic (not elevated) accepts it.
            if (uri.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || IsElevated()) { ViaExplorer(uri); return; }
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
            catch (System.ComponentModel.Win32Exception)
            {
                // The link handler is missing or broken: Steam can still be asked directly; anything else via Explorer.
                if (!TrySteamDirect(game)) ViaExplorer(uri);
            }
            return;
        }
        if (game.ExePath is not { } exe || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
            throw new FileNotFoundException("Game executable not found.", game.ExePath);
        // ShellExecute keeps Windows behavior for games that request elevation in their manifest.
        Process.Start(new ProcessStartInfo(exe, game.Args ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
    }

    private static void ViaExplorer(string uri) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + uri + "\"") { UseShellExecute = false })?.Dispose();

    private static bool IsElevated()
    {
        try { using var id = System.Security.Principal.WindowsIdentity.GetCurrent(); return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
        catch (Exception) { return false; }
    }

    // steam.exe -applaunch <id>, from the path Steam records for the user.
    private static bool TrySteamDirect(GameEntry game)
    {
        if (game.Store != GameStores.Steam || !game.Id.All(char.IsDigit)) return false;
        var exe = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamExe") as string;
        if (exe is null || !File.Exists(exe)) return false;
        Process.Start(new ProcessStartInfo(exe, "-applaunch " + game.Id) { UseShellExecute = false })?.Dispose();
        return true;
    }
}
