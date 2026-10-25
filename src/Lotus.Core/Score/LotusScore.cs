using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Score;

// LOTUS SCORE: short, repeatable CPU and GPU tests; nothing in Windows is changed.
public interface ICpuBenchmark { double Run(int threads, TimeSpan duration, CancellationToken ct); } // work units per second
// Runs 1 warm-up + `passes` measured passes in a visible scene window; null = no hardware GPU. onPass(i) fires as pass i starts (0 = warm-up).
public interface IGpuBenchmark { string? Adapter(); IReadOnlyList<double>? Run(int passes, TimeSpan each, Action<int> onPass, CancellationToken ct); }
// Live picture of what the CPU test is rendering (BGRA pixels), shown in the page while it runs.
public interface IFramePreview { int Width { get; } int Height { get; } uint[] Pixels { get; } int Version { get; } }
public interface IScoreHistory { IReadOnlyList<ScoreRun> Load(); void Save(IReadOnlyList<ScoreRun> runs); }
public interface IScoreMachine { string Cpu(); string Gpu(); }

// Null fields = test not run (the user chose a subset); Total only when the full set ran.
public sealed record ScoreRun(DateTime Date, int? Cpu1, int? CpuAll, int? Gpu, int? Total, double VariationPct, string CpuName, string GpuName);
public sealed record ScoreReference(string Tier, string Example, int Cpu1, int CpuAll, int Gpu)
{
    public int Total => LotusScale.Total(Cpu1, CpuAll, Gpu);
}
public sealed record Measured(double Median, double VariationPct);
public sealed record CompareRow(string Label, string Detail, int Total, double Percent, bool IsYou, string TotalText = "");
public sealed record HistoryRow(string Date, string Summary);
public sealed record PartRow(string Name, string Kind, string Values, string VsYou, double Percent, bool IsYours);

public static class LotusScale
{
    // Raw-to-points factors. Calibrated once on the development PC (Ryzen 5 5500 + RTX 5060) so its
    // points match the reference row below; the other rows come from public relative-performance data. Estimates.
    public const double Cpu1Factor = 1400 / 23450.0, CpuAllFactor = 10500 / 194700.0, GpuFactor = 11500 / 10380.0;
    private const double RefCpu1 = 1500, RefCpuAll = 11000, RefGpu = 9000; // 10 000 points = mid-range gamer PC

    public static int Points(double raw, double factor) => (int)Math.Round(raw * factor);
    // Weighted geometric mean: GPU matters most for games, then multi-core, then single-core.
    public static int Total(int cpu1, int cpuAll, int? gpu)
    {
        if (cpu1 <= 0 || cpuAll <= 0) return 0;
        var log = 0.2 * Math.Log(cpu1 / RefCpu1) + 0.3 * Math.Log(cpuAll / RefCpuAll);
        log = gpu is > 0 ? log + 0.5 * Math.Log(gpu.Value / RefGpu) : log / 0.5;
        return (int)Math.Round(10000 * Math.Exp(log));
    }

    public static Measured Summarize(IReadOnlyList<double> passes)
    {
        if (passes.Count == 0) return new(0, 0);
        var sorted = passes.Order().ToArray();
        var median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        return new(median, median > 0 ? (sorted[^1] - sorted[0]) / median * 100 : 0);
    }

    public static IReadOnlyList<ScoreReference> References { get; } =
    [
        new("ScoreTierEntry", "Intel Core i3-10100 · GTX 1650", 1200, 5500, 3500),
        new("ScoreTierBudget", "Ryzen 5 3600 · GTX 1660 Super", 1300, 9000, 6000),
        new("ScoreTierMid", "Ryzen 5 5600 · RTX 3060", 1500, 11000, 9000),
        new("ScoreTierMidHigh", "Ryzen 5 7600 · RTX 4060 Ti", 1850, 14500, 13500),
        new("ScoreTierHigh", "Ryzen 7 7800X3D · RTX 4070 Super", 1800, 18000, 20000),
        new("ScoreTierEnthusiast", "Core i9-14900K · RTX 4090", 2250, 38000, 36000),
    ];
}

public sealed class LotusScoreViewModel(ICpuBenchmark cpu, IGpuBenchmark gpu, IScoreHistory history, IScoreMachine machine, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    public static readonly TimeSpan PassLength = TimeSpan.FromSeconds(3);
    public const int Passes = 3;
    private CancellationTokenSource? cts;
    private ScoreRun? last;
    private bool runCpu1 = true, runCpuAll = true, runGpu = true;
    private string partQuery = "";

    public ITextCatalog Text { get; } = text;
    public IFramePreview? Preview => cpu as IFramePreview;
    public bool Busy { get; private set; }
    public bool Idle => !Busy;
    public bool ShowCpuPreview { get; private set; }
    public bool HasResult => last is not null;
    public bool NoHistory => History.Count == 0;
    public double Progress { get; private set; }
    public string Phase { get; private set; } = "";
    public string Total => last?.Total is { } t ? t.ToString("N0", Culture) : "—";
    public string Cpu1 => Points(last?.Cpu1);
    public string CpuAll => Points(last?.CpuAll);
    public string Gpu => Points(last?.Gpu);
    public string Verdict { get; private set; } = "";
    public string Reliability { get; private set; } = "";
    public string MachineText { get; private set; } = "";
    public string StartLabel => Text[Busy ? "ScoreCancel" : "ScoreStart"];
    public bool CanStart => Busy || runCpu1 || runCpuAll || runGpu;
    public bool RunCpu1 { get => runCpu1; set { if (SetProperty(ref runCpu1, value)) OnPropertyChanged(nameof(CanStart)); } }
    public bool RunCpuAll { get => runCpuAll; set { if (SetProperty(ref runCpuAll, value)) OnPropertyChanged(nameof(CanStart)); } }
    public bool RunGpu { get => runGpu; set { if (SetProperty(ref runGpu, value)) OnPropertyChanged(nameof(CanStart)); } }
    public ObservableCollection<CompareRow> Compare { get; } = [];
    public ObservableCollection<HistoryRow> History { get; } = [];
    public ObservableCollection<PartRow> PartResults { get; } = [];
    public string PartQuery { get => partQuery; set { if (SetProperty(ref partQuery, value ?? "")) SearchParts(); } }
    public string PartHint { get; private set; } = "";
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    private string Points(int? value) => value is { } v ? v.ToString("N0", Culture) : "—";

    public void Refresh()
    {
        try
        {
            MachineText = Text.Format("ScoreMachine", machine.Cpu(), machine.Gpu());
            last ??= history.Load().FirstOrDefault();
            if (last?.Total is { } t && Verdict.Length == 0) Verdict = VerdictFor(t);
        }
        catch (Exception ex) { log(ex); }
        Rebuild(); SearchParts();
    }

    // Start or cancel.
    public async Task ToggleAsync()
    {
        if (Busy) { cts?.Cancel(); return; }
        if (!CanStart) return;
        cts = new CancellationTokenSource(); var ct = cts.Token;
        var ui = SynchronizationContext.Current;
        bool doCpu1 = runCpu1, doCpuAll = runCpuAll, doGpu = runGpu && gpu.Adapter() is not null;
        var stages = (doCpu1 ? 1 : 0) + (doCpuAll ? 1 : 0) + (doGpu ? 1 : 0); var stage = 0;
        Busy = true; Progress = 0; Reliability = ""; Verdict = ""; Notify();
        try
        {
            var threads = Environment.ProcessorCount;
            Measured? single = null, multi = null, graphics = null;
            ShowCpuPreview = Preview is not null; Notify();
            if (doCpu1) single = await MeasureCpu(p => Text.Format("ScorePhaseCpu1", p), () => cpu.Run(1, PassLength, ct), stage++, stages, ct);
            if (doCpuAll) multi = await MeasureCpu(p => Text.Format("ScorePhaseCpuAll", threads, p), () => cpu.Run(threads, PassLength, ct), stage++, stages, ct);
            ShowCpuPreview = false; Notify();
            if (doGpu)
            {
                var gpuStage = stage++;
                void OnPass(int i)
                {
                    void Update() { Phase = PhaseText(p => Text.Format("ScorePhaseGpu", p), i); Progress = StageProgress(gpuStage, stages, i); Notify(); }
                    if (ui is null) Update(); else ui.Post(_ => Update(), null);
                }
                var passes = await Task.Run(() => gpu.Run(Passes, PassLength, OnPass, ct), ct);
                if (passes is { Count: > 0 }) graphics = LotusScale.Summarize(passes);
            }
            ct.ThrowIfCancellationRequested();
            int? c1 = single is null ? null : LotusScale.Points(single.Median, LotusScale.Cpu1Factor);
            int? ca = multi is null ? null : LotusScale.Points(multi.Median, LotusScale.CpuAllFactor);
            int? g = graphics is { Median: > 0 } ? LotusScale.Points(graphics.Median, LotusScale.GpuFactor) : null;
            // Total only for the full set (GPU may be absent when the PC has no hardware GPU).
            var full = c1 is not null && ca is not null && (g is not null || gpu.Adapter() is null);
            int? total = full ? LotusScale.Total(c1!.Value, ca!.Value, g) : null;
            var variation = new[] { single?.VariationPct ?? 0, multi?.VariationPct ?? 0, graphics?.VariationPct ?? 0 }.Max();
            last = new ScoreRun(DateTime.Now, c1, ca, g, total, Math.Round(variation, 1), machine.Cpu(), machine.Gpu());
            history.Save([last, .. history.Load().Take(19)]);
            Verdict = total is { } t ? VerdictFor(t) : Text["ScorePartial"];
            Reliability = Text.Format(variation < 3 ? "ScoreReliable" : "ScoreUnstable", variation);
            Phase = Text["ScoreDone"];
            if (runGpu && gpu.Adapter() is null) Phase += " " + Text["ScoreNoGpu"];
        }
        catch (OperationCanceledException) { Phase = Text["ScoreCancelled"]; }
        catch (Exception ex) { log(ex); Phase = Text["ScoreFailed"]; }
        finally { Busy = false; ShowCpuPreview = false; Progress = 100; cts?.Dispose(); cts = null; Rebuild(); SearchParts(); }
    }

    private string PhaseText(Func<string, string> phase, int i) => phase(i == 0 ? Text["ScoreWarmup"] : Text.Format("ScorePass", i, Passes));
    private static double StageProgress(int stage, int stages, int pass) => (stage * (Passes + 1) + pass) * 100.0 / (Math.Max(1, stages) * (Passes + 1));

    // One warm-up pass plus three measured passes; the median is the result.
    private async Task<Measured> MeasureCpu(Func<string, string> phase, Func<double> pass, int stage, int stages, CancellationToken ct)
    {
        var values = new List<double>();
        for (var i = 0; i <= Passes; i++)
        {
            ct.ThrowIfCancellationRequested();
            Phase = PhaseText(phase, i); Progress = StageProgress(stage, stages, i); Notify();
            var v = await Task.Run(pass, ct);
            if (i > 0) values.Add(v);
        }
        return LotusScale.Summarize(values);
    }

    private string VerdictFor(int total)
    {
        var refs = LotusScale.References;
        var below = refs.Count(r => r.Total <= total);
        return below == 0 ? Text["ScoreVerdictLow"] : Text.Format("ScoreVerdict", Text[refs[below - 1].Tier], below, refs.Count);
    }

    private void Rebuild()
    {
        Compare.Clear();
        var rows = LotusScale.References.Select(r => (Label: Text[r.Tier], Detail: r.Example, r.Total, You: false)).ToList();
        if (last?.Total is { } total) rows.Add((Text["ScoreYou"], $"{last.CpuName} · {last.GpuName}", total, true));
        var max = Math.Max(1, rows.Max(r => r.Total));
        foreach (var r in rows.OrderByDescending(r => r.Total)) Compare.Add(new(r.Label, r.Detail, r.Total, r.Total * 100.0 / max, r.You, r.Total.ToString("N0", Culture)));
        History.Clear();
        try
        {
            foreach (var h in history.Load())
                History.Add(new(h.Date.ToString("g", Culture), Text.Format("ScoreHistoryLine", Points(h.Total), Points(h.Cpu1), Points(h.CpuAll), Points(h.Gpu))));
        }
        catch (Exception ex) { log(ex); }
        Notify();
    }

    // ---- Individual parts: estimated points of one CPU or GPU, compared with this PC ----
    private void SearchParts()
    {
        PartResults.Clear();
        var terms = ScoreParts.Terms(partQuery);
        if (terms.Length == 0) { PartHint = Text["ScorePartHint"]; OnPropertyChanged(nameof(PartHint)); return; }
        var myCpu = last?.CpuAll ?? ScoreParts.Find(machine.Cpu(), PartKind.Cpu)?.Multi;
        var myCpu1 = last?.Cpu1 ?? ScoreParts.Find(machine.Cpu(), PartKind.Cpu)?.Single;
        var myGpu = last?.Gpu ?? ScoreParts.Find(machine.Gpu(), PartKind.Gpu)?.Gpu;
        var matches = ScoreParts.All.Where(p => terms.All(t => ScoreParts.Normalize(p.Name).Contains(t))).Take(12).ToList();
        foreach (var p in matches)
        {
            var isCpu = p.Kind == PartKind.Cpu;
            var value = isCpu ? p.Multi : p.Gpu;
            var mine = isCpu ? myCpu : myGpu;
            var top = ScoreParts.All.Where(x => x.Kind == p.Kind).Max(x => isCpu ? x.Multi : x.Gpu);
            var values = isCpu ? Text.Format("ScorePartCpuValues", p.Single.ToString("N0", Culture), p.Multi.ToString("N0", Culture)) : Text.Format("ScorePartGpuValues", p.Gpu.ToString("N0", Culture));
            var yours = ScoreParts.Find(isCpu ? machine.Cpu() : machine.Gpu(), p.Kind) == p;
            var vs = yours ? Text["ScorePartYours"] : mine is > 0 ? Text.Format("ScorePartVs", (value - mine.Value) * 100.0 / mine.Value) : "";
            PartResults.Add(new(p.Name, isCpu ? "CPU" : "GPU", values, vs, value * 100.0 / top, yours));
        }
        PartHint = matches.Count == 0 ? Text["ScorePartNone"] : myCpu1 is null && myGpu is null ? Text["ScorePartRunFirst"] : "";
        OnPropertyChanged(nameof(PartHint));
    }

    // Online comparison: opens public sites with this PC's parts (no scraping, no data sent by Lotus).
    public IReadOnlyList<(string Name, string Url)> OnlineLinks()
    {
        var q = Uri.EscapeDataString($"{machine.Cpu()} {machine.Gpu()}");
        var g = Uri.EscapeDataString(machine.Gpu());
        return
        [
            ("pc-builds", "https://pc-builds.com/pt/fps-calculator/"),
            ("3DMark", $"https://www.3dmark.com/search#advanced?test=spy%20P&gpuName={g}"),
            ("How Many FPS", $"https://www.google.com/search?q=site%3Ahowmanyfps.com+{q}"),
        ];
    }

    private void Notify()
    {
        foreach (var p in new[] { nameof(Busy), nameof(Idle), nameof(ShowCpuPreview), nameof(HasResult), nameof(Progress), nameof(Phase), nameof(Total), nameof(Cpu1), nameof(CpuAll), nameof(Gpu),
                     nameof(Verdict), nameof(Reliability), nameof(MachineText), nameof(StartLabel), nameof(NoHistory), nameof(CanStart) })
            OnPropertyChanged(p);
    }
    public void RefreshLanguage()
    {
        if (Busy) return; // phase texts update on the next step
        Verdict = last is null ? "" : last.Total is { } t ? VerdictFor(t) : Text["ScorePartial"];
        Reliability = last is null || Reliability.Length == 0 ? "" : Text.Format(last.VariationPct < 3 ? "ScoreReliable" : "ScoreUnstable", last.VariationPct);
        Phase = "";
        Refresh();
    }
}
