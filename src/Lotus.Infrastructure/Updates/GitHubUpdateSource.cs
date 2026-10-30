using System.Net.Http.Headers;
using System.Text.Json;
using Lotus.Core.Updates;

namespace Lotus.Infrastructure.Updates;

// Lotus releases on GitHub (public API, no sign-in). Only the installer published with a release is downloaded, only
// over https from GitHub, and only when the release also publishes its SHA-256.
public sealed class GitHubUpdateSource(string owner = "JoeAfricano", string repo = "Lotus", HttpClient? http = null) : IUpdateSource
{
    private readonly HttpClient http = http ?? Create();
    private static readonly string[] Hosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "api.github.com"];

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lotus", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<UpdateRelease?> LatestAsync(CancellationToken token)
    {
        using var response = await http.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases?per_page=15", token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        UpdateRelease? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (UpdateVersions.Parse(tag) is not { } version || (best is not null && version <= best.Version)) continue;
            var assets = r.GetProperty("assets").EnumerateArray().ToList();
            var exe = assets.FirstOrDefault(a => Name(a) is { } n && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && n.Contains("Setup", StringComparison.OrdinalIgnoreCase));
            if (exe.ValueKind != JsonValueKind.Object || Url(exe) is not { } url) continue;
            var name = Name(exe)!;
            string? sha = null;
            var shaAsset = assets.FirstOrDefault(a => string.Equals(Name(a), name + ".sha256", StringComparison.OrdinalIgnoreCase));
            if (shaAsset.ValueKind == JsonValueKind.Object && Url(shaAsset) is { } shaUrl)
                sha = (await http.GetStringAsync(shaUrl, token)).Trim().Split(' ', '\t', '\n', '\r')[0];
            best = new UpdateRelease(version, tag, r.GetProperty("name").GetString() ?? tag, r.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : DateTimeOffset.Now,
                url, Path.GetFileName(name), exe.TryGetProperty("size", out var size) ? size.GetInt64() : 0, sha,
                r.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "");
        }
        return best;
    }

    public async Task DownloadAsync(UpdateRelease release, Stream target, IProgress<long> progress, CancellationToken token)
    {
        if (!Trusted(release.InstallerUrl)) throw new InvalidOperationException("Untrusted download address");
        using var response = await http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && !Trusted(final.ToString())) throw new InvalidOperationException("Untrusted download address");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[81920];
        long done = 0; var lastReport = 0L;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            done += read;
            if (done - lastReport > 256 * 1024) { progress.Report(done); lastReport = done; }
        }
        progress.Report(done);
    }

    private static string? Name(JsonElement a) => a.TryGetProperty("name", out var n) ? n.GetString() : null;
    private static string? Url(JsonElement a) => a.TryGetProperty("browser_download_url", out var u) && u.GetString() is { } s && Trusted(s) ? s : null;
    private static bool Trusted(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && Hosts.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase));
}
