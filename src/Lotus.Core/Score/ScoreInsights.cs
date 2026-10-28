namespace Lotus.Core.Score;

// Deeper reading of a Lotus Score run, in the spirit of FPS calculators (pc-builds, How Many FPS) and 3DMark:
// expected FPS per game, resolution and preset; which part holds the PC back; graphics/CPU sub-scores with a rank
// against the parts database; stability between passes. Everything here is an estimate derived from the measured
// points — never a measurement of the game itself.

// FPS a game reaches on the calibration PC (Ryzen 5 5500 + RTX 5060) at 1080p "High": GPU-limited FPS, the CPU-limited
// ceiling, and an engine cap when the game has one. Approximate values from public benchmarks (±20%).
// Basis: for searched games, the recommended parts the numbers come from (else null = benchmark table).
public sealed record GameDemand(string Name, double GpuFps, double CpuFps, int? Cap = null, string? SteamAppId = null, string? Basis = null);

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

    public static IReadOnlyList<GameDemand> Games { get; } =
    [
        new("Counter-Strike 2", 330, 360, SteamAppId: "730"), new("Valorant", 600, 520), new("Fortnite", 165, 230),
        new("Apex Legends", 190, 240, 300, "1172470"), new("League of Legends", 480, 420), new("Dota 2", 260, 230, SteamAppId: "570"),
        new("PUBG: Battlegrounds", 150, 185, SteamAppId: "578080"), new("Rainbow Six Siege", 330, 380, SteamAppId: "359550"),
        new("Rocket League", 360, 400, SteamAppId: "252950"), new("Marvel Rivals", 110, 145, SteamAppId: "2767030"),
        new("Call of Duty: Warzone", 120, 170, SteamAppId: "1962663"), new("Helldivers 2", 85, 95, SteamAppId: "553850"),
        new("Grand Theft Auto V", 160, 170, 188, "271590"), new("Red Dead Redemption 2", 85, 140, SteamAppId: "1174180"),
        new("Cyberpunk 2077", 85, 120, SteamAppId: "1091500"), new("The Witcher 3: Wild Hunt", 120, 140, SteamAppId: "292030"),
        new("Elden Ring", 95, 120, 60, "1245620"), new("Baldur's Gate 3", 110, 95, SteamAppId: "1086940"),
        new("Hogwarts Legacy", 80, 88, SteamAppId: "990080"), new("Starfield", 60, 75, SteamAppId: "1716740"),
        new("Alan Wake 2", 55, 120), new("Black Myth: Wukong", 55, 110, SteamAppId: "2358720"),
        new("God of War", 110, 160, SteamAppId: "1593500"), new("Forza Horizon 5", 140, 180, SteamAppId: "1551360"),
        new("Monster Hunter Wilds", 50, 70, SteamAppId: "2246340"), new("Palworld", 100, 110, SteamAppId: "1623730"),
        new("Persona 5 Royal", 240, 300, SteamAppId: "1687950"), new("No Rest for the Wicked", 75, 90, SteamAppId: "1371980"),
        new("Minecraft", 300, 250), new("Stardew Valley", 500, 500, 144, "413150"),
    ];

    // Expected FPS for one cell. The slower of the GPU and CPU limits wins (a soft minimum, as real frames overlap a little).
    public static FpsCell Estimate(GameDemand g, Resolution r, Preset p, int single, int multi, int gpu)
    {
        if (single <= 0 || multi <= 0 || gpu <= 0) return new(0, Limiter.Balanced);
        var gpuFps = g.GpuFps * (gpu / RefGpu) * ResolutionLoad[(int)r] * PresetGpu[(int)p];
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
