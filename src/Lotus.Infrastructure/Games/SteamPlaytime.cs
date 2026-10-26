using Microsoft.Win32;

namespace Lotus.Infrastructure.Games;

// Minutes played per Steam game, read from the local Steam config of the most recently used account
// (userdata\<account>\config\localconfig.vdf → Software\Valve\Steam\apps\<appid>\Playtime). Read-only, offline.
public static class SteamPlaytime
{
    public static IReadOnlyDictionary<string, int> Read(string? steamPath = null)
    {
        try
        {
            var steam = steamPath ?? (!OperatingSystem.IsWindows() ? null : Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string)?.Replace('/', Path.DirectorySeparatorChar);
            var userdata = steam is null ? null : Path.Combine(steam, "userdata");
            if (userdata is null || !Directory.Exists(userdata)) return new Dictionary<string, int>();
            var config = Directory.EnumerateDirectories(userdata).Select(d => Path.Combine(d, "config", "localconfig.vdf")).Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            return config is null ? new Dictionary<string, int>() : Parse(File.ReadAllText(config));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new Dictionary<string, int>(); }
    }

    public static IReadOnlyDictionary<string, int> Parse(string vdf)
    {
        var apps = VdfNode.Parse(vdf).Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps");
        var result = new Dictionary<string, int>();
        if (apps is null) return result;
        foreach (var (id, node) in apps.Children)
            if (id.All(char.IsDigit) && int.TryParse(node.Get("Playtime"), out var minutes) && minutes > 0) result[id] = minutes;
        return result;
    }
}
