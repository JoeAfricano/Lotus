// Builds data/steam-demand.json: the most owned Steam games and what their recommended PC means on the Lotus scale.
// Run on a PC with internet:  dotnet run tools/BuildSteamBase.cs -- [count=1000]
// List: SteamSpy "all" (most owned first). Per game: the Steam store's recommended requirements (minimum when there are
// none), read by the same parser Lotus uses at runtime (ScoreInsights.FromRequirements). Only part names and the
// derived numbers are kept, never the requirement text. Store answers are cached in %TEMP%\lotus-steam-base so a
// stopped run continues where it was. Steam allows about 200 store requests per 5 minutes: one every 1.6 s.
#:project ../src/Lotus.Core/Lotus.Core.csproj
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lotus.Core.Score;

var count = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 1000;
var root = Directory.GetCurrentDirectory();
var outPath = Path.Combine(root, "data", "steam-demand.json");
var cache = Path.Combine(Path.GetTempPath(), "lotus-steam-base");
Directory.CreateDirectory(cache);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Lotus-base-builder/1.0");

async Task<JsonNode?> Get(string url, string file, bool polite)
{
    var path = Path.Combine(cache, file);
    if (File.Exists(path))
        try { return JsonNode.Parse(await File.ReadAllTextAsync(path)); }
        catch (JsonException) { File.Delete(path); } // a cached error page: ask again
    for (var attempt = 0; attempt < 4; attempt++)
    {
        if (polite) await Task.Delay(1600);
        using var res = await http.GetAsync(url);
        if ((int)res.StatusCode is 429 or 403) { Console.WriteLine($"  limite da Steam ({(int)res.StatusCode}), aguardando 60 s"); await Task.Delay(60_000); continue; }
        if (!res.IsSuccessStatusCode) return null;
        var text = await res.Content.ReadAsStringAsync();
        JsonNode? node;
        try { node = JsonNode.Parse(text); } catch (JsonException) { Console.WriteLine($"  resposta inválida de {url}"); return null; }
        await File.WriteAllTextAsync(path, text); // only valid answers are cached
        return node;
    }
    return null;
}

// Most owned first (SteamSpy pages hold 1,000 games each).
var list = new List<(string Id, string Name)>();
for (var page = 0; list.Count < count * 5 && page < 5; page++)
{
    if (await Get($"https://steamspy.com/api.php?request=all&page={page}", $"spy-all-{page}.json", false) is not JsonObject all) break;
    list.AddRange(all.Select(kv => (kv.Key, (string?)kv.Value?["name"] ?? kv.Key)).Where(x => x.Item1.All(char.IsAsciiDigit)));
}
Console.WriteLine($"{list.Count} jogos na lista; meta: {count} com requisitos reconhecidos.");

var games = new JsonArray();
int seen = 0, skipped = 0;
var unmatched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // graphics lines no part matched: what to add to ScoreParts next
foreach (var (id, spyName) in list)
{
    if (games.Count >= count) break;
    seen++;
    var store = await Get($"https://store.steampowered.com/api/appdetails?appids={id}&l=english&cc=us", $"app-{id}.json", true);
    var data = store?[id]?["success"]?.GetValue<bool>() == true ? store[id]!["data"] : null;
    if (data is null || (string?)data["type"] != "game") { skipped++; continue; }
    var name = ((string?)data["name"] ?? spyName).Trim();
    var req = data["pc_requirements"];
    var html = req is JsonObject o ? ((string?)o["recommended"] ?? (string?)o["minimum"]) : null;
    if (html is null) { skipped++; continue; }
    var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(html.Replace("<br>", "\n").Replace("<li>", "\n"), "<[^>]+>", " "));
    if (ScoreInsights.FromRequirements(name, id, text) is not { } d)
    {
        skipped++;
        var line = text.Split('\n').FirstOrDefault(l => Regex.IsMatch(l, "Graphics|Video", RegexOptions.IgnoreCase))?.Trim();
        if (line is { Length: > 0 }) { line = Regex.Replace(line, @"\s+", " "); unmatched[line] = unmatched.GetValueOrDefault(line) + 1; }
        continue;
    }
    games.Add(new JsonObject { ["appId"] = id, ["name"] = name, ["gpu"] = d.GpuFps, ["cpu"] = d.CpuFps, ["ramGb"] = d.RamGb, ["basis"] = d.Basis });
    if (games.Count % 50 == 0) Console.WriteLine($"  {games.Count} prontos ({seen} vistos, {skipped} sem dados úteis)");
}

var doc = new JsonObject
{
    ["schema"] = 1,
    ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
    ["source"] = "Steam store recommended requirements (part names and Lotus-scale numbers only); list: SteamSpy most owned",
    ["games"] = games,
};
await File.WriteAllTextAsync(outPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
var report = Path.Combine(root, "artifacts", "steam-base-unmatched.txt");
Directory.CreateDirectory(Path.GetDirectoryName(report)!);
await File.WriteAllLinesAsync(report, unmatched.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value}\t{kv.Key}"));
Console.WriteLine($"Placas não reconhecidas: {unmatched.Count} linhas em {report}");
Console.WriteLine($"Pronto: {games.Count} jogos em {outPath} ({seen} vistos, {skipped} sem requisitos reconhecidos ou não são jogos).");
