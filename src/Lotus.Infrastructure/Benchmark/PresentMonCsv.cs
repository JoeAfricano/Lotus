using System.Globalization;
using Lotus.Core.Benchmark;
using Microsoft.VisualBasic.FileIO;

namespace Lotus.Infrastructure.Benchmark;

public static class PresentMonCsv
{
    public const int MaximumRows = 300_000;
    public const long MaximumBytes = 128 * 1024 * 1024;
    public static ParsedCapture Read(string path, int pid, CancellationToken token = default)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("CSV exceeds 128 MiB.");
        using var csv = new TextFieldParser(path);
        csv.SetDelimiters(","); csv.HasFieldsEnclosedInQuotes = true;
        var headers = csv.ReadFields() ?? throw new InvalidDataException("Empty CSV.");
        int Column(string name) => Array.IndexOf(headers, name);
        var process = Column("ProcessID"); var chain = Column("SwapChainAddress");
        var time = Column("TimeInMs"); var inMilliseconds = time >= 0;
        if (time < 0) time = Column("TimeInSeconds");
        var interval = Column("MsBetweenPresents");
        if (interval < 0) interval = Column("msBetweenPresents");
        var type = Column("FrameType");
        if (new[] { process, chain, time, interval }.Any(i => i < 0)) throw new InvalidDataException("Unsupported PresentMon CSV schema.");
        var streams = new Dictionary<string, List<FrameSample>>();
        var lossColumns = new[] { Column("EtwEventsLost"), Column("EtwBuffersLost"), Column("OverflowedPresents") };
        long?[] losses = new long?[3];
        var types = new HashSet<string>(); var total = 0; var invalid = 0; var truncated = false;
        while (!csv.EndOfData)
        {
            token.ThrowIfCancellationRequested();
            if (total >= MaximumRows) { truncated = true; break; }
            total++;
            var row = csv.ReadFields();
            if (row is null || row.Length != headers.Length) { invalid++; continue; }
            if (!int.TryParse(row[process], out var rowPid) || rowPid != pid) continue;
            for (var i = 0; i < lossColumns.Length; i++)
                if (lossColumns[i] >= 0 && long.TryParse(row[lossColumns[i]], out var loss) && loss >= 0)
                    losses[i] = Math.Max(losses[i] ?? 0, loss);
            if (type >= 0) types.Add(row[type]);
            // FrameType is preserved as metadata. Frame-generated rows cannot be silently mixed.
            if (type >= 0 && row[type] != "Application") continue;
            if (!streams.TryGetValue(row[chain], out var frames))
            {
                if (streams.Count >= 64) { truncated = true; break; }
                streams[row[chain]] = frames = [];
                // First interval spans an unknown point before capture; always discard it.
                continue;
            }
            if (!double.TryParse(row[time], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                !double.TryParse(row[interval], NumberStyles.Float, CultureInfo.InvariantCulture, out var dt) ||
                !double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(dt) || dt <= 0 ||
                (frames.Count > 0 && seconds / (inMilliseconds ? 1000 : 1) <= frames[^1].TimeSeconds)) { invalid++; continue; }
            if (inMilliseconds) seconds /= 1000;
            frames.Add(new(seconds, dt));
        }
        var dominant = streams.OrderByDescending(s => s.Value.Count).ThenBy(s => s.Key).FirstOrDefault();
        IReadOnlyList<FrameSample> samples = dominant.Value ?? [];
        return new(dominant.Key ?? "", Math.Max(0, streams.Count - 1), invalid, total, samples,
            invalid == 0 && !truncated && !losses.Any(v => v > 0) ? BenchmarkMath.Calculate(samples) : null,
            types.Count == 0 ? "Unavailable" : string.Join(", ", types.Order()), truncated, losses[0], losses[1], losses[2]);
    }
}
