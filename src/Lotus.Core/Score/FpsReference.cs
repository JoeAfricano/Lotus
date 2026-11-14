using System.Globalization;
using System.Text.Json.Nodes;

namespace Lotus.Core.Score;

// The FPS reference table (data\fps-reference.json): games, lab numbers at 1080p, confidence and sources, with its date.
// A copy ships inside Lotus; a newer copy from the official repository may replace it after validation (never trusted
// blindly: schema, sizes and value ranges are checked, and anything odd keeps the copy in use).
public sealed record FpsSource(string Label, string Url);
public sealed record FpsReferenceData(DateOnly Date, IReadOnlyList<GameDemand> Games, IReadOnlyList<GameDemand> Pending, IReadOnlyDictionary<string, FpsSource> Sources)
{
    public int Count => Games.Count + Pending.Count;
}

public static class FpsReference
{
    public const string RemoteUrl = "https://raw.githubusercontent.com/JoeAfricano/Lotus/main/data/fps-reference.json";
    public static readonly TimeSpan CheckEvery = TimeSpan.FromDays(7);
    public static readonly TimeSpan RequirementsKeep = TimeSpan.FromDays(30);
    private static readonly HashSet<string> Confidences = ["alta", "media", "baixa"];
    // Ryzen 5 5500 class against the 9800X3D the labs use, in processor-bound scenes (documented estimate).
    public const double CpuShareOfLab = 0.62;

    private static FpsReferenceData? builtin;
    public static FpsReferenceData Builtin()
    {
        if (builtin is not null) return builtin;
        using var stream = typeof(FpsReference).Assembly.GetManifestResourceStream("fps-reference.json") ?? throw new InvalidOperationException("fps-reference.json missing");
        using var reader = new StreamReader(stream);
        return builtin = Parse(reader.ReadToEnd()) ?? throw new InvalidDataException("Built-in fps-reference.json is invalid");
    }

    // Weekly at most, and only when the player left the option on.
    public static bool ShouldCheck(DateTimeOffset? lastCheck, DateTimeOffset now, bool enabled) => enabled && (lastCheck is null || now - lastCheck >= CheckEvery || lastCheck > now);

    // A remote copy replaces the one in use only when it is valid and not older.
    public static bool IsNewer(FpsReferenceData? candidate, FpsReferenceData current) => candidate is not null && candidate.Date >= current.Date && candidate.Count >= current.Count / 2;

    public static FpsReferenceData? Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || (int?)root["schema"] != 1) return null;
            if (!DateOnly.TryParseExact((string?)root["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
            var sources = new Dictionary<string, FpsSource>();
            if (root["sources"] is JsonObject src)
                foreach (var (id, node) in src)
                {
                    var url = (string?)node?["url"];
                    if (url is null || !url.StartsWith("https://", StringComparison.Ordinal)) return null;
                    sources[id] = new((string?)node?["label"] ?? id, url);
                }
            if (root["games"] is not JsonArray games || games.Count is < 20 or > 2000) return null;
            var ready = new List<GameDemand>(); var pending = new List<GameDemand>();
            foreach (var g in games.OfType<JsonObject>())
            {
                var name = ((string?)g["name"])?.Trim();
                var confidence = (string?)g["confidence"] ?? "";
                if (string.IsNullOrEmpty(name) || name.Length > 120 || !Confidences.Contains(confidence)) return null;
                var appId = (string?)g["steamAppId"];
                if (appId is not null && !appId.All(char.IsAsciiDigit)) return null;
                double[]? lab = null;
                if (g["lab1080"] is JsonArray l)
                {
                    if (l.Count != 4) return null;
                    lab = [.. l.Select(v => (double?)v ?? 0)];
                    if (lab.Any(v => v is < 0 or > 2000) || lab[0] <= 0) return null;
                }
                var gpuHigh = (double?)g["gpuHigh"] ?? 0; var cpuCap = (double?)g["cpuCap"] ?? 0; var cap = (int?)g["cap"];
                if (gpuHigh is < 0 or > 2000 || cpuCap is < 0 or > 2000 || cap is < 20 or > 1000) return null;
                var sourceIds = g["sources"] is JsonArray s ? s.Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToArray() : [];
                if (sourceIds.Any(id => !sources.ContainsKey(id))) return null;
                var basisLabel = string.Join(" · ", sourceIds.Select(id => sources[id].Label));
                if (lab is null && gpuHigh <= 0) { pending.Add(new(name, 0, 0, cap, appId, Confidence: confidence, Store: (string?)g["store"], SourceIds: sourceIds)); continue; }
                var high = lab is { } lb && lb[2] > 0 ? lb[2] : gpuHigh;
                var cpu = cpuCap > 0 ? cpuCap : lab is { } lc ? Math.Round(lc[0] * CpuShareOfLab) : high * 1.2;
                ready.Add(new(name, high, cpu, cap, appId, Lab: lab, Confidence: confidence, Store: (string?)g["store"], SourceIds: sourceIds) { SourceText = basisLabel });
            }
            return ready.Count < 20 ? null : new(date, ready, pending, sources);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return null; }
    }
}
