using System.Runtime.Versioning;
using Lotus.Core.Profile;
using Lotus.Infrastructure.Games;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Profile;

// Steam: config\loginusers.vdf (most recent account) and config\avatarcache\<SteamID64>.png. Read-only, local only.
[SupportedOSPlatform("windows")]
public sealed class LocalProfileSource(string? steamPath = null, string? lotusFolder = null) : IProfileSource
{
    private string Folder => lotusFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus");

    public SteamProfile? ReadSteam()
    {
        var steam = steamPath ?? (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        if (steam is null) return null;
        var file = Path.Combine(steam, "config", "loginusers.vdf");
        if (!File.Exists(file)) return null;
        var users = VdfNode.Parse(File.ReadAllText(file)).Child("users");
        if (users is null || users.Children.Count == 0) return null;
        var (id, user) = users.Children.Select(kv => (kv.Key, kv.Value)).OrderByDescending(u => u.Value.Get("MostRecent") == "1")
            .ThenByDescending(u => long.TryParse(u.Value.Get("Timestamp"), out var t) ? t : 0).First();
        if (user.Get("PersonaName") is not { Length: > 0 } name) return null;
        var avatar = Path.Combine(steam, "config", "avatarcache", id + ".png");
        return new(name.Trim(), File.Exists(avatar) ? avatar : null);
    }

    public bool SteamInstalled() => (steamPath ?? Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string) is { } s && Directory.Exists(s);

    // Copies the chosen picture so the profile keeps working if the original moves. Images only.
    public string ImportPicture(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")) throw new InvalidOperationException("Not a picture.");
        if (new FileInfo(sourcePath).Length > 20L << 20) throw new InvalidOperationException("Picture too large.");
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.EnumerateFiles(Folder, "profile-picture.*")) File.Delete(old);
        var target = Path.Combine(Folder, "profile-picture" + ext);
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }
}
