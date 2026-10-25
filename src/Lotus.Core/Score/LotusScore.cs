using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Score;

// LOTUS SCORE: short, repeatable CPU and GPU tests; nothing in Windows is changed.
public interface ICpuBenchmark { double Run(int threads, TimeSpan duration, CancellationToken ct); } // work units per second
public interface IGpuBenchmark { string? Adapter(); double? Run(TimeSpan duration, CancellationToken ct); } // null = no hardware GPU
public interface IScoreHistory { IReadOnlyList<ScoreRun> Load(); void Save(IReadOnlyList<ScoreRun> runs); }
public interface IScoreMachine { string Cpu(); string Gpu(); }

public sealed record ScoreRun(DateTime Date, int Cpu1, int CpuAll, int? Gpu, int Total, double VariationPct, string CpuName, string GpuName);
public sealed record ScoreReference(string Tier, string Example, int Cpu1, int CpuAll, int Gpu)
{
    public int Total => LotusScale.Total(Cpu1, CpuAll, Gpu);
}
public sealed record Measured(double Median, double VariationPct);
public sealed record CompareRow(string Label, string Detail, int Total, double Percent, bool IsYou);
public sealed record HistoryRow(string Date, string Summary);

public static class LotusScale
{
    // Raw-to-points factors. Calibrated once on the development PC (Ryzen 5 5500 + RTX 5060) so its
    // points match the reference row below; the other rows come from public relative-performance data. Estimates.
    public const double Cpu1Factor = 1400 / 23330.0, CpuAllFactor = 10500 / 198600.0, GpuFactor = 11500 / 10160.0;
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
        new("ScoreTierMidHigh", "Ryzen 5 5500 · RTX 5060", 1400, 10500, 11500),
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

    public ITextCatalog Text { get; } = text;
    public bool Busy { get; private set; }
    public bool HasResult => last is not null;
    public bool NoHistory => History.Count == 0;
    public double Progress { get; private set; }
    public string Phase { get; private set; } = "";
    public string Total => last is null ? "—" : last.Total.ToString("N0", Culture);
    public string Cpu1 => last is null ? "—" : last.Cpu1.ToString("N0", Culture);
    public string CpuAll => last is null ? "—" : last.CpuAll.ToString("N0", Culture);
    public string Gpu => last?.Gpu is { } g ? g.ToString("N0", Culture) : "—";
    public string Verdict { get; private set; } = "";
    public string Reliability { get; private set; } = "";
    public string MachineText { get; private set; } = "";
    public string StartLabel => Text[Busy ? "ScoreCancel" : "ScoreStart"];
    public ObservableCollection<CompareRow> Compare { get; } = [];
    public ObservableCollection<HistoryRow> History { get; } = [];
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);

    public void Refresh()
    {
        try
        {
            MachineText = Text.Format("ScoreMachine", machine.Cpu(), machine.Gpu());
            var runs = history.Load(); last ??= runs.FirstOrDefault();
            if (last is not null && Verdict.Length == 0) Verdict = VerdictFor(last.Total);
        }
        catch (Exception ex) { log(ex); }
        Rebuild();
    }

    // Start or cancel.
    public async Task ToggleAsync()
    {
        if (Busy) { cts?.Cancel(); return; }
        cts = new CancellationTokenSource(); var ct = cts.Token;
        Busy = true; Progress = 0; Reliability = ""; Notify();
        try
        {
            var threads = Environment.ProcessorCount;
            var single = await Measure(p => Text.Format("ScorePhaseCpu1", p), () => cpu.Run(1, PassLength, ct), 0, ct);
            var multi = await Measure(p => Text.Format("ScorePhaseCpuAll", threads, p), () => cpu.Run(threads, PassLength, ct), 1, ct);
            Measured? graphics = null;
            if (gpu.Adapter() is not null)
                graphics = await Measure(p => Text.Format("ScorePhaseGpu", p), () => gpu.Run(PassLength, ct) ?? 0, 2, ct);
            ct.ThrowIfCancellationRequested();
            var c1 = LotusScale.Points(single.Median, LotusScale.Cpu1Factor);
            var ca = LotusScale.Points(multi.Median, LotusScale.CpuAllFactor);
            int? g = graphics is { Median: > 0 } ? LotusScale.Points(graphics.Median, LotusScale.GpuFactor) : null;
            var variation = new[] { single.VariationPct, multi.VariationPct, graphics?.VariationPct ?? 0 }.Max();
            last = new ScoreRun(DateTime.Now, c1, ca, g, LotusScale.Total(c1, ca, g), Math.Round(variation, 1), machine.Cpu(), machine.Gpu());
            history.Save([last, .. history.Load().Take(19)]);
            Verdict = VerdictFor(last.Total);
            Reliability = variation < 3 ? Text.Format("ScoreReliable", variation) : Text.Format("ScoreUnstable", variation);
            Phase = Text["ScoreDone"];
            if (g is null) Phase += " " + Text["ScoreNoGpu"];
        }
        catch (OperationCanceledException) { Phase = Text["ScoreCancelled"]; }
        catch (Exception ex) { log(ex); Phase = Text["ScoreFailed"]; }
        finally { Busy = false; Progress = 100; cts?.Dispose(); cts = null; Rebuild(); }
    }

    // One warm-up pass plus three measured passes; the median is the result.
    private async Task<Measured> Measure(Func<string, string> phase, Func<double> pass, int stage, CancellationToken ct)
    {
        var values = new List<double>();
        for (var i = 0; i <= Passes; i++)
        {
            ct.ThrowIfCancellationRequested();
            Phase = phase(i == 0 ? Text["ScoreWarmup"] : Text.Format("ScorePass", i, Passes));
            Progress = (stage * (Passes + 1) + i) * 100.0 / (3 * (Passes + 1)); Notify();
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
        if (last is not null) rows.Add((Text["ScoreYou"], $"{last.CpuName} · {last.GpuName}", last.Total, true));
        var max = Math.Max(1, rows.Max(r => r.Total));
        foreach (var r in rows.OrderByDescending(r => r.Total)) Compare.Add(new(r.Label, r.Detail, r.Total, r.Total * 100.0 / max, r.You));
        History.Clear();
        try
        {
            foreach (var h in history.Load())
                History.Add(new(h.Date.ToString("g", Culture), Text.Format("ScoreHistoryLine", h.Total, h.Cpu1, h.CpuAll, h.Gpu is { } hg ? hg.ToString("N0", Culture) : "—")));
        }
        catch (Exception ex) { log(ex); }
        Notify();
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
        foreach (var p in new[] { nameof(Busy), nameof(HasResult), nameof(Progress), nameof(Phase), nameof(Total), nameof(Cpu1), nameof(CpuAll), nameof(Gpu), nameof(Verdict), nameof(Reliability), nameof(MachineText), nameof(StartLabel), nameof(NoHistory) })
            OnPropertyChanged(p);
    }
    public void RefreshLanguage()
    {
        if (Busy) return; // phase texts update on the next step
        Verdict = last is null ? "" : VerdictFor(last.Total);
        Reliability = last is null || Reliability.Length == 0 ? "" : Text.Format(last.VariationPct < 3 ? "ScoreReliable" : "ScoreUnstable", last.VariationPct);
        Phase = "";
        Refresh();
    }
}
