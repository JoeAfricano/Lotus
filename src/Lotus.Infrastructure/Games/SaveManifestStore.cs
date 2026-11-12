using System.Net;
using System.Text.Json;
using Lotus.Core.Saves;

namespace Lotus.Infrastructure.Games;

// The Ludusavi manifest on this PC: downloaded from the project's GitHub (MIT), at most once a week and only when it
// changed (ETag), read straight from the download into a compact index of Windows save folders
// (%LOCALAPPDATA%\Lotus\cache\save-manifest.json, a few MB). Lotus starts from that index; the YAML is never kept.
public sealed class SaveManifestStore(string? cacheFile = null, HttpClient? http = null)
{
    public const string Url = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromDays(7);
    public const int MinimumGames = 1000; // a smaller answer is an error page or a broken file: the current index stays

    private readonly string file = cacheFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "cache", "save-manifest.json");
    private readonly HttpClient client = http ?? Create();

    public sealed record Index(int Version, DateTimeOffset Fetched, string? ETag, List<ManifestGame> Games);
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static HttpClient Create()
    {
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(2) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lotus/1.0 (+desktop game helper)");
        return c;
    }

    public Index? LoadIndex()
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var stream = File.OpenRead(file);
            var index = JsonSerializer.Deserialize<Index>(stream, Json);
            return index is { Version: FormatVersion, Games.Count: >= MinimumGames } ? index : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    public SaveManifest? Load() => LoadIndex() is { } i ? new SaveManifest(i.Games) : null;

    // New manifest when the cached one is a week old (or missing) and the file changed; null = keep what is loaded.
    public async Task<SaveManifest?> RefreshAsync(bool force = false, CancellationToken cancel = default)
    {
        var current = LoadIndex();
        if (!force && current is not null && DateTimeOffset.UtcNow - current.Fetched < RefreshEvery) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        if (current?.ETag is { Length: > 0 } etag && System.Net.Http.Headers.EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && current is not null)
        {
            Save(current with { Fetched = DateTimeOffset.UtcNow });
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var reader = new StreamReader(body);
        var games = await Task.Run(() => LudusaviManifest.Parse(reader), cancel).ConfigureAwait(false);
        if (games.Count < MinimumGames) return null;
        Save(new(FormatVersion, DateTimeOffset.UtcNow, response.Headers.ETag?.ToString(), [.. games]));
        return new SaveManifest(games);
    }

    private void Save(Index index)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        using (var stream = File.Create(temp)) JsonSerializer.Serialize(stream, index, Json);
        File.Move(temp, file, overwrite: true);
    }
}
