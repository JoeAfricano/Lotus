using System.Net.Http;
using System.Text.Json.Nodes;

namespace Lotus.App.Controls;

// Box art for games that are not on Steam (Epic, Riot, Mojang...): the lead image of the game's Wikipedia article
// (English, then Portuguese), read through Wikipedia's public REST summary (no key). Saved once in
// %LOCALAPPDATA%\Lotus\Covers\wiki; a miss is remembered too, so nothing is asked twice.
public static class WikiCover
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Covers", "wiki");
    private static readonly Dictionary<string, Task<string?>> Pending = [];
    private static readonly HashSet<string> Hosts = ["upload.wikimedia.org", "thumb.wikimedia.org"];

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lotus/1.0 (https://github.com/JoeAfricano/Lotus)"); // Wikimedia asks bots for a project link
        return c;
    }

    // The local file of the cover, or null when there is none (an earlier miss included).
    public static Task<string?> GetAsync(string title)
    {
        lock (Pending)
        {
            if (!Pending.TryGetValue(title, out var task)) Pending[title] = task = Task.Run(() => FetchAsync(title));
            return task;
        }
    }

    private static string Safe(string title) => string.Concat(title.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private static async Task<string?> FetchAsync(string title)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, Safe(title) + ".jpg");
            var miss = file + ".none";
            if (File.Exists(file)) return file;
            if (File.Exists(miss) && DateTime.Now - File.GetLastWriteTime(miss) < TimeSpan.FromDays(30)) return null;
            foreach (var lang in new[] { "en", "pt" })
            {
                foreach (var name in new[] { title + " (video game)", title })
                {
                    var url = $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(name.Replace(' ', '_'))}";
                    using var resp = await Http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) continue;
                    var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
                    if (node?["type"]?.GetValue<string>() == "disambiguation") continue;
                    var image = node?["thumbnail"]?["source"]?.GetValue<string>() ?? node?["originalimage"]?["source"]?.GetValue<string>();
                    if (image is null || !Uri.TryCreate(image, UriKind.Absolute, out var u) || !Hosts.Contains(u.Host)) continue;
                    byte[] bytes;
                    try { bytes = await Http.GetByteArrayAsync(u.GetLeftPart(UriPartial.Path)); } catch (HttpRequestException) { continue; } // one refused picture does not stop the others
                    if (bytes.Length < 2000) continue;
                    await File.WriteAllBytesAsync(file, bytes);
                    return file;
                }
            }
            // Some articles expose no lead image (non-free art): the first logo or cover in the article's media, if any.
            using (var media = await Http.GetAsync($"https://en.wikipedia.org/api/rest_v1/page/media-list/{Uri.EscapeDataString(title.Replace(' ', '_'))}"))
            {
                if (media.IsSuccessStatusCode && JsonNode.Parse(await media.Content.ReadAsStringAsync())?["items"] is JsonArray items)
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        var name = item["title"]?.GetValue<string>() ?? "";
                        if (item["type"]?.GetValue<string>() != "image" || !(name.Contains("logo", StringComparison.OrdinalIgnoreCase) || name.Contains("cover", StringComparison.OrdinalIgnoreCase) || name.Contains("box", StringComparison.OrdinalIgnoreCase))) continue;
                        var src = item["srcset"]?[0]?["src"]?.GetValue<string>();
                        if (src is null || !Uri.TryCreate(src.StartsWith("//") ? "https:" + src : src, UriKind.Absolute, out var u) || !Hosts.Contains(u.Host)) continue;
                        byte[] bytes;
                        try { bytes = await Http.GetByteArrayAsync(u.GetLeftPart(UriPartial.Path)); } catch (HttpRequestException) { continue; }
                        if (bytes.Length < 2000) continue;
                        await File.WriteAllBytesAsync(file, bytes);
                        return file;
                    }
            }
            await File.WriteAllTextAsync(miss, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Serilog.Log.Debug(ex, "Wikipedia cover {Title}", title);
        }
        return null;
    }
}
