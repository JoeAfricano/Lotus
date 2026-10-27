using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Manual games persisted as JSON; written to a temp file and moved so a crash never leaves a half-written list.
public sealed class JsonManualGameStore(string path) : IManualGameStore
{
    private sealed record Item(string ExePath, string Name, string? Args);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public IReadOnlyList<GameEntry> Load()
    {
        if (!File.Exists(path)) return [];
        var items = JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(path)) ?? [];
        return items.Where(i => !string.IsNullOrWhiteSpace(i.ExePath) && i.ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(i => new GameEntry(GameStores.Manual, i.ExePath.ToLowerInvariant(), i.Name, Path.GetDirectoryName(i.ExePath), i.ExePath, Args: i.Args)).ToList();
    }

    public void Save(IReadOnlyList<GameEntry> games) =>
        Write(path, games.Where(g => g.ExePath is not null).Select(g => new Item(g.ExePath!, g.Name, g.Args)).ToList());

    private string HiddenPath => Path.Combine(Path.GetDirectoryName(path)!, "games-hidden.json");
    public IReadOnlyList<string> LoadHidden() => File.Exists(HiddenPath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HiddenPath)) ?? [] : [];
    public void SaveHidden(IReadOnlyList<string> keys) => Write(HiddenPath, keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

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
