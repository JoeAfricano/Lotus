namespace Lotus.Core.Benchmark;

// The numbers of the Lotus overlay window, from every frametime of the game (RivaTuner's frametime pipe). Frames are
// stored as they come (one write, no allocation) and summarized only when the window redraws (a few times a second).
public sealed class OverlayFrames
{
    public const int Capacity = 8192;          // about 1–2 minutes of frames: the lows follow the recent play, not the whole session
    public const int GraphFrames = 200;
    private readonly float[] ring = new float[Capacity];
    private long count;
    private long summarizedCount = -1;
    private Summary? cached;
    private readonly object gate = new();

    public void Add(double milliseconds)
    {
        if (!(milliseconds > 0 && milliseconds < 5000)) return;
        lock (gate) { ring[count % Capacity] = (float)milliseconds; count++; }
    }

    public sealed record Summary(double Fps, double Low1, double Low01, double FrametimeMs, IReadOnlyList<float> Graph, double Average);

    // FPS follows the last quarter-second; lows and average keep their longer recent-play window.
    public Summary? Summarize()
    {
        float[] frames;
        long version;
        lock (gate)
        {
            if (summarizedCount == count) return cached;
            version = count;
            var n = (int)Math.Min(count, Capacity);
            if (n < 10) return null;
            frames = new float[n];
            for (var i = 0; i < n; i++) frames[i] = ring[(count - n + i) % Capacity];
        }
        double second = 0; var inSecond = 0;
        for (var i = frames.Length - 1; i >= 0 && second < 250; i--) { second += frames[i]; inSecond++; }
        var fps = second > 0 ? inSecond * 1000 / second : 0;
        var sorted = (float[])frames.Clone();
        Array.Sort(sorted);
        double Low(double share)
        {
            var take = Math.Max(1, (int)Math.Ceiling(sorted.Length * share));
            double sum = 0; for (var i = sorted.Length - take; i < sorted.Length; i++) sum += sorted[i];
            return 1000 / (sum / take);
        }
        var graph = frames[Math.Max(0, frames.Length - GraphFrames)..];
        double total = 0; foreach (var f in frames) total += f;
        var result = new Summary(fps, Low(0.01), Low(0.001), frames[^1], graph, frames.Length * 1000 / total);
        lock (gate) { if (version == count) { cached=result; summarizedCount=version; } }
        return result;
    }

    public void Clear() { lock (gate) { count = 0; summarizedCount = -1; cached=null; } }
}
