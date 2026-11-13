namespace Lotus.Core.Score;

// Deeper reading of a Lotus Score run, in the spirit of FPS calculators (pc-builds, How Many FPS) and 3DMark:
// expected FPS per game, resolution and preset; which part holds the PC back; graphics/CPU sub-scores with a rank
// against the parts database; stability between passes. Everything here is an estimate derived from the measured
// points — never a measurement of the game itself.

// FPS a game reaches on the calibration PC (Ryzen 5 5500 + RTX 5060) at 1080p "High": GPU-limited FPS, the CPU-limited
// ceiling, and an engine cap when the game has one. Approximate values from public benchmarks (±20%).
// Basis: for searched games, the recommended parts the numbers come from (else null = benchmark table).
public sealed record GameDemand(string Name, double GpuFps, double CpuFps, int? Cap = null, string? SteamAppId = null, string? Basis = null,
    IReadOnlyList<double>? Lab = null, string Confidence = "", string? Store = null, IReadOnlyList<string>? SourceIds = null)
{
    // Lab numbers at 1080p per preset (Low → Ultra; 0 = not measured) from the reference table, when there are any.
    public string SourceText { get; init; } = "";
}

public enum Resolution { P1080, P1440, P2160 }
public enum Preset { Low, Medium, High, Ultra }
public enum Limiter { Gpu, Cpu, Balanced, Cap }

public sealed record FpsCell(int Fps, Limiter Limiter);
public sealed record Bottleneck(Resolution Resolution, Limiter Limiter, int Percent);

public static class ScoreInsights
{
    // Calibration PC points (see LotusScale): Ryzen 5 5500 = 1 400 / 10 500, RTX 5060 = 11 500.
    private const double RefSingle = 1400, RefMulti = 10500, RefGpu = 11500;
    private static readonly double[] ResolutionLoad = [1.0, 0.68, 0.36];           // GPU FPS left at 1440p / 4K
    private static readonly double[] PresetGpu = [1.85, 1.38, 1.0, 0.78];          // Low → Ultra
    private static readonly double[] PresetCpu = [1.12, 1.05, 1.0, 0.94];

    // The reference table in use (built-in copy, or a newer validated one from the official repository).
    public static FpsReferenceData Reference { get; private set; } = FpsReference.Builtin();
    public static IReadOnlyList<GameDemand> Games { get; private set; } = Reference.Games;
    private static readonly List<GameDemand> resolved = []; // requirement-based entries already worked out
    public static void Use(FpsReferenceData data) { Reference = data; Games = [.. data.Games, .. resolved.Where(r => data.Pending.Any(p => p.Name == r.Name))]; }
    public static void AddResolved(GameDemand g) { if (Games.Any(x => x.Name == g.Name)) return; resolved.Add(g); Games = [.. Games, g]; }

    // Expected FPS for one cell. The slower of the GPU and CPU limits wins (a soft minimum, as real frames overlap a little).
    public static FpsCell Estimate(GameDemand g, Resolution r, Preset p, int single, int multi, int gpu)
    {
        if (single <= 0 || multi <= 0 || gpu <= 0) return new(0, Limiter.Balanced);
        // With lab numbers per preset they are used as measured (scaled to this GPU); otherwise the High value and fixed preset steps.
        var basePreset = g.Lab is { Count: 4 } lab && lab[(int)p] > 0 ? lab[(int)p] : g.GpuFps * PresetGpu[(int)p];
        var gpuFps = basePreset * (gpu / RefGpu) * ResolutionLoad[(int)r];
        var cpuFps = g.CpuFps * Math.Pow(single / RefSingle, 0.6) * Math.Pow(multi / RefMulti, 0.4) * PresetCpu[(int)p];
        const double k = 6;
        var fps = Math.Pow(Math.Pow(gpuFps, -k) + Math.Pow(cpuFps, -k), -1 / k);
        if (g.Cap is { } cap && fps >= cap) return new(cap, Limiter.Cap);
        var ratio = gpuFps / cpuFps;
        var limiter = ratio < 0.9 ? Limiter.Gpu : ratio > 1.1 ? Limiter.Cpu : Limiter.Balanced;
        return new((int)Math.Round(fps), limiter);
    }

    // How much the slower part holds the faster one back on a typical game mix (pc-builds style), per resolution.
    public static IReadOnlyList<Bottleneck> Bottlenecks(int single, int multi, int gpu)
    {
        var list = new List<Bottleneck>();
        if (single <= 0 || multi <= 0 || gpu <= 0) return list;
        foreach (var r in Enum.GetValues<Resolution>())
        {
            double cpuSide = 0, gpuSide = 0;
            foreach (var g in Games)
            {
                var gpuFps = g.GpuFps * (gpu / RefGpu) * ResolutionLoad[(int)r];
                var cpuFps = g.CpuFps * Math.Pow(single / RefSingle, 0.6) * Math.Pow(multi / RefMulti, 0.4);
                gpuSide += Math.Log(gpuFps); cpuSide += Math.Log(cpuFps);
            }
            var ratio = Math.Exp((gpuSide - cpuSide) / Games.Count); // > 1: the GPU could do more than the CPU feeds it
            var pct = (int)Math.Round((1 - Math.Min(ratio, 1 / ratio)) * 100);
            list.Add(new(r, pct < 8 ? Limiter.Balanced : ratio > 1 ? Limiter.Cpu : Limiter.Gpu, pct));
        }
        return list;
    }

    // 3DMark-style sub-scores: graphics = the GPU points; CPU = single and multi blended (1 500 / 11 000 → 10 000).
    public static int CpuScore(int single, int multi) =>
        single <= 0 || multi <= 0 ? 0 : (int)Math.Round(10000 * Math.Exp(0.4 * Math.Log(single / 1500.0) + 0.6 * Math.Log(multi / 11000.0)));

    // Share of the parts in the database this result beats (GPU among GPUs, CPU among CPUs).
    public static int BeatsGpus(int gpu) => Beats(ScoreParts.All.Where(p => p.Kind == PartKind.Gpu).Select(p => (double)p.Gpu), gpu);
    public static int BeatsCpus(int single, int multi) => Beats(ScoreParts.All.Where(p => p.Kind == PartKind.Cpu).Select(p => (double)CpuScore(p.Single, p.Multi)), CpuScore(single, multi));
    private static int Beats(IEnumerable<double> values, double mine)
    {
        var all = values.ToList();
        return all.Count == 0 || mine <= 0 ? 0 : (int)Math.Round(all.Count(v => v < mine) * 100.0 / all.Count);
    }

    // Stability between measured passes (3DMark stress tests pass at 97% frame-rate stability).
    public static double Stability(double variationPct) => Math.Round(Math.Clamp(100 - variationPct, 0, 100), 1);
    public static bool StabilityPasses(double variationPct) => Stability(variationPct) >= 97;

    // Any game, from its recommended PC requirements (Steam): those parts are taken to run it at about 60 FPS at
    // 1080p High, then scaled to this PC. Null when no graphics card in the text is known to the parts database.
    public static GameDemand? FromRequirements(string name, string appId, string? requirements)
    {
        if (string.IsNullOrWhiteSpace(requirements)) return null;
        var lines = requirements.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        static string? Line(string[] all, params string[] keys) => all.FirstOrDefault(l => keys.Any(k => l.Contains(k, StringComparison.OrdinalIgnoreCase)));
        var gpuLine = Line(lines, "Placa de vídeo", "Placa de video", "Gráficos", "Graphics", "Video") ?? requirements;
        var cpuLine = Line(lines, "Processador", "Processor", "CPU") ?? "";
        var gpus = Alternatives(gpuLine).Select(a => ScoreParts.Find(a, PartKind.Gpu)).OfType<PartScore>().ToList();
        if (gpus.Count == 0) return null;
        var cpus = Alternatives(cpuLine).Select(a => ScoreParts.Find(a, PartKind.Cpu)).OfType<PartScore>().ToList();
        var gpuFps = 60 * RefGpu / gpus.Average(p => p.Gpu);
        var cpuFps = cpus.Count == 0 ? gpuFps * 1.4 : 75 * CpuScore((int)RefSingle, (int)RefMulti) / cpus.Average(p => (double)CpuScore(p.Single, p.Multi));
        var basis = string.Join(" · ", new[] { string.Join(" / ", gpus.Select(p => p.Name).Distinct()), string.Join(" / ", cpus.Select(p => p.Name).Distinct()) }.Where(s => s.Length > 0));
        return new(name, Math.Round(gpuFps), Math.Round(cpuFps), SteamAppId: appId, Basis: basis);
    }
    private static IEnumerable<string> Alternatives(string line) => line.Split(["/", " or ", " ou ", ",", "|"], StringSplitOptions.RemoveEmptyEntries);

    // The player's installed games that the table knows (by Steam id or name), then the rest of the table.
    public static IReadOnlyList<GameDemand> Ordered(IEnumerable<(string Name, string? SteamId)> installed)
    {
        var mine = new List<GameDemand>();
        foreach (var (name, id) in installed)
            if (Games.FirstOrDefault(g => (id is not null && g.SteamAppId == id) || string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)) is { } hit && !mine.Contains(hit))
                mine.Add(hit);
        return [.. mine, .. Games.Except(mine).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
