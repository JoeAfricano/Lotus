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
            var info = uri.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", uri) { UseShellExecute = false }
                : new ProcessStartInfo(uri) { UseShellExecute = true };
            Process.Start(info)?.Dispose();
            return;
        }
        if (game.ExePath is not { } exe || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
            throw new FileNotFoundException("Game executable not found.", game.ExePath);
        // ShellExecute keeps Windows behavior for games that request elevation in their manifest.
        Process.Start(new ProcessStartInfo(exe, game.Args ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
    }
}
