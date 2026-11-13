using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lotus.Core.Score;

namespace Lotus.Infrastructure.Catalog;

// Keeps the FPS reference table current: at most once a week (or on "Atualizar agora"), asks the official repository
// for a newer copy with ETag (nothing is downloaded when unchanged), validates it and keeps it in the Lotus cache.
// Any failure leaves the table in use as it is, quietly.
public sealed class FpsReferenceUpdater(string? cacheDir = null, HttpClient? http = null)
{
    private readonly string dir = cacheDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "cache");
    private readonly HttpClient client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
    private string DataFile => Path.Combine(dir, "fps-reference.json");
    private string StateFile => Path.Combine(dir, "fps-reference.state.json");
    private sealed record State(DateTimeOffset? LastCheck, string? ETag);

    // At start: a saved copy newer than the built-in one takes over.
    public void LoadSaved()
    {
        try
        {
            if (File.Exists(DataFile) && FpsReference.Parse(File.ReadAllText(DataFile)) is { } saved && FpsReference.IsNewer(saved, ScoreInsights.Reference))
                ScoreInsights.Use(saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Debug(ex, "FPS reference cache"); }
    }

    // True when a newer table is now in use.
    public async Task<bool> CheckAsync(bool enabled, bool force, CancellationToken token = default)
    {
        var state = Read();
        if (!force && !FpsReference.ShouldCheck(state.LastCheck, DateTimeOffset.Now, enabled)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FpsReference.RemoteUrl);
            if (state.ETag is { Length: > 0 } tag && File.Exists(DataFile)) request.Headers.TryAddWithoutValidation("If-None-Match", tag);
            request.Headers.UserAgent.ParseAdd("Lotus/1.0 (https://github.com/JoeAfricano/Lotus)");
            using var response = await client.SendAsync(request, token);
            Write(state with { LastCheck = DateTimeOffset.Now });
            if (response.StatusCode == HttpStatusCode.NotModified || !response.IsSuccessStatusCode) return false;
            var json = await response.Content.ReadAsStringAsync(token);
            if (json.Length > 2_000_000 || FpsReference.Parse(json) is not { } fresh || !FpsReference.IsNewer(fresh, ScoreInsights.Reference)) return false;
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(DataFile, json, token);
            Write(new(DateTimeOffset.Now, response.Headers.ETag?.Tag));
            ScoreInsights.Use(fresh);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { Serilog.Log.Debug(ex, "FPS reference check"); return false; }
    }

    private State Read()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) ?? new(null, null) : new(null, null); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(null, null); }
    }
    private void Write(State s)
    {
        try { Directory.CreateDirectory(dir); File.WriteAllText(StateFile, JsonSerializer.Serialize(s)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Debug(ex, "FPS reference state"); }
    }
}
