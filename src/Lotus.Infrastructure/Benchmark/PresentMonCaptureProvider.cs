using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lotus.Core.Benchmark;
using Serilog;

namespace Lotus.Infrastructure.Benchmark;

public sealed class PresentMonCaptureProvider(string root, ILogger logger) : IFrameCaptureProvider
{
    public const string Version = "2.6.0";
    public const string Digest = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
    public static string ToolPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lotus", "Tools", "PresentMon", Version, "PresentMon-2.6.0-x64.exe");
    private readonly SemaphoreSlim gate = new(1);
    public Task<BenchmarkSession> CaptureAsync(CaptureRequest request, CancellationToken stop) => Task.Run(async () =>
    {
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Capture already active.");
        try { return await Capture(request, stop); }
        finally { gate.Release(); }
    });
    private async Task<BenchmarkSession> Capture(CaptureRequest request, CancellationToken stop)
    {
        if (request.Seconds is < 10 or > 120) throw new ArgumentOutOfRangeException(nameof(request.Seconds));
        stop.ThrowIfCancellationRequested();
        var id = Guid.NewGuid(); var started = DateTimeOffset.UtcNow; var watch = Stopwatch.StartNew();
        var directory = Path.Combine(root, id.ToString("N")); Directory.CreateDirectory(directory);
        var raw = Path.Combine(directory, "frames.csv"); var diagnostics = new StringBuilder();
        var status = "Failed"; ParsedCapture? parsed = null; var hash = ""; CaptureResourceUsage? usage = null;
        int? exitCode = null;
        // Durable intent survives application crashes; raw files remain reprocessable even if DB save fails.
        await File.WriteAllTextAsync(Path.Combine(directory, "request.json"), JsonSerializer.Serialize(request));
        try
        {
            using var binary = File.Open(ToolPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(binary)).Equals(Digest, StringComparison.Ordinal))
                throw new InvalidDataException("PresentMon integrity check failed.");
            using var target = Process.GetProcessById(request.Target.Pid);
            if (target.StartTime.ToUniversalTime() != request.Target.ProcessStartedUtc.UtcDateTime ||
                !string.Equals(target.MainModule?.FileName, request.Target.Executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Target identity changed.");
            // A session left open by a Lotus that crashed survives the process and can use up the provider's session
            // slots: PresentMon then records nothing and says nothing (Stellar Blade study, 25/11/2026). Close those first.
            var ledger = new EtwSessionLedger(root);
            // Elevated recording: one helper run stops every stuck "Lotus*" session (logman), including ones the ledger never
            // saw (study scripts, a ledger lost with a crash). Asked only when there is a reason: a pending ledger entry or the
            // last recording got no frame / lost events.
            if (request.Elevated && EtwSessionCleanup.NeedsCleanup(root, ledger.Pending()))
            {
                var code = await RunElevatedHelper(["--etw-clean"]);
                if (code is >= 0 and <= 100)
                {
                    diagnostics.AppendLine($"Stuck Lotus ETW sessions stopped: {code}.");
                    logger.Information("ETW cleanup: {Count} stuck Lotus sessions stopped", code);
                    foreach (var stale in ledger.Pending()) ledger.Remove(stale);
                    EtwSessionCleanup.ClearSuspect(root);
                }
                else logger.Warning("ETW cleanup did not run (exit {Code})", code);
            }
            foreach (var stale in ledger.Pending())
            {
                try
                {
                    using var cleaner = Start(["--session_name", stale, "--terminate_existing_session"], request.Elevated);
                    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await cleaner.WaitForExitAsync(wait.Token);
                    diagnostics.AppendLine($"Closed a session left open: {stale}.");
                }
                catch (Exception ex) when (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }) { logger.Warning(ex, "Stale ETW session not closed {Session}", stale); }
                ledger.Remove(stale);
            }
            var sessionName = "Lotus-" + id.ToString("N");
            ledger.Add(sessionName);
            var args = new[] { "--process_id", request.Target.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--output_file", raw, "--session_name", sessionName, "--timed", request.Seconds.ToString(),
                "--terminate_after_timed", "--no_console_stats", "--no_track_input", "--track_frame_type", "--track_pc_latency" }; // GPU tracking on: MsGPUBusy per frame (cause of each hitch)
            using var collector = Start(args, request.Elevated);
            var collectionClock = Stopwatch.StartNew();
            using var owner = Process.GetCurrentProcess(); var ownerCpu = owner.TotalProcessorTime.TotalSeconds;
            double lotusWs = 0, collectorWs = 0, collectorCpu = 0, lotusCpu = 0, sampleSeconds = 0;
            int samples = 0, collectorSamples = 0;
            logger.Information("Benchmark capture started {Session} {TargetPid} {CollectorPid} {Elevated}", id, target.Id, collector.Id, request.Elevated);
            Task? output = null, error = null;
            if (!request.Elevated)
            {
                output = Drain(collector.StandardOutput, diagnostics);
                error = Drain(collector.StandardError, diagnostics);
            }
            var stopped = false; var targetExited = false; var limitExceeded = false; var noFrames = false;
            while (!collector.HasExited)
            {
                if (collectionClock.Elapsed.TotalSeconds >= samples + 1)
                {
                    owner.Refresh(); lotusWs += owner.WorkingSet64 / 1048576d; lotusCpu = owner.TotalProcessorTime.TotalSeconds - ownerCpu;
                    samples++; sampleSeconds = collectionClock.Elapsed.TotalSeconds;
                    try { collector.Refresh(); collectorWs += collector.WorkingSet64 / 1048576d; collectorCpu = collector.TotalProcessorTime.TotalSeconds; collectorSamples++; }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                }
                targetExited = target.HasExited;
                limitExceeded = File.Exists(raw) && new FileInfo(raw).Length > PresentMonCsv.MaximumBytes;
                // No frame in the first 10 s: say so now instead of waiting for an empty result.
                if (!stopped && collectionClock.Elapsed.TotalSeconds >= FirstFrameSeconds && !PresentMonCsv.HasRows(raw)) noFrames = true;
                if (!stopped && (noFrames || stop.IsCancellationRequested || targetExited || limitExceeded || collectionClock.Elapsed.TotalSeconds > request.Seconds + 15))
                {
                    stopped = true;
                    try
                    {
                        // Only our random session is stopped. Never stop another tool's ETW session.
                        using var terminator = Start(["--session_name", sessionName, "--terminate_existing_session"], request.Elevated);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await terminator.WaitForExitAsync(timeout.Token);
                    }
                    catch (Exception ex) { logger.Warning(ex, "Benchmark stop request failed {Session}", id); diagnostics.AppendLine("Stop request failed; waiting for timed collector shutdown."); }
                }
                if (collectionClock.Elapsed.TotalSeconds > request.Seconds + 30)
                {
                    try { collector.Kill(); } catch (System.ComponentModel.Win32Exception) { }
                    if (!collector.HasExited) throw new IOException("Collector did not stop; its timed session requires inspection.");
                }
                await Task.Delay(200);
            }
            if (output is not null) await Task.WhenAll(output, error!);
            if (samples > 0) usage = new(sampleSeconds, samples, 100 * lotusCpu / sampleSeconds / Environment.ProcessorCount,
                lotusWs / samples, collectorSamples > 0 ? 100 * collectorCpu / sampleSeconds / Environment.ProcessorCount : null,
                collectorSamples > 0 ? collectorWs / collectorSamples : null);
            logger.Information("Benchmark collector exited {Session} {ExitCode}", id, collector.ExitCode);
            ledger.Remove(sessionName);
            exitCode = collector.ExitCode;
            diagnostics.AppendLine($"ExitCode={collector.ExitCode}; StopRequested={stop.IsCancellationRequested}; TargetExited={targetExited}; SizeLimit={limitExceeded}; NoFramesIn{FirstFrameSeconds}s={noFrames}");
            diagnostics.AppendLine("ETW loss counters unavailable in validated 2.6.0 CSV. Results are conditional on capture integrity.");
            if (request.Elevated) diagnostics.AppendLine("Elevated collector: console diagnostics unavailable.");
            if (File.Exists(raw) && new FileInfo(raw).Length > 0)
            {
                using var stream = File.OpenRead(raw); hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                parsed = PresentMonCsv.Read(raw, request.Target.Pid);
            }
            diagnostics.AppendLine($"ETW: events={parsed?.LostEvents?.ToString() ?? "unavailable"}; buffers={parsed?.LostBuffers?.ToString() ?? "unavailable"}; overflow={parsed?.OverflowedPresents?.ToString() ?? "unavailable"}");
            var reportedLoss = parsed?.LostEvents > 0 || parsed?.LostBuffers > 0 || parsed?.OverflowedPresents > 0;
            status = noFrames ? "NoFrames" : limitExceeded || parsed?.Truncated == true ? "LimitExceeded" : collector.ExitCode != 0 ? "Failed" :
                reportedLoss ? "EventLoss" : parsed?.Statistics is not null ? "Completed" : "InsufficientData";
            if (status is not ("Completed" or "EventLoss") && parsed is not null) parsed = parsed with { Statistics = null };
            if (status is "NoFrames" or "EventLoss") EtwSessionCleanup.MarkSuspect(root, status); // the next recording cleans first
        }
        catch (OperationCanceledException) { status = "Cancelled"; }
        catch (Exception ex)
        {
            logger.Warning(ex, "Benchmark failed {Session}", id); diagnostics.AppendLine(ex.Message);
            if (ex is FileNotFoundException) status = "ToolMissing";
            else if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }) status = "Cancelled";
        }
        if (diagnostics.ToString().Contains("access denied", StringComparison.OrdinalIgnoreCase)) status = "AccessDenied";
        var result = new BenchmarkSession(id, request.Target, started, watch.Elapsed.TotalSeconds, status, Version, BenchmarkMath.Version,
            raw, hash, request.Conditions, request.HardwareJson, parsed, diagnostics.ToString())
        {
            ResourceUsage = usage, CollectorExitCode = exitCode,
            // Reflex PC latency per frame (median and 95th percentile), only when the game gives the markers.
            Extras = Lotus.Core.Games.LimiterTrial.Latency(parsed?.PcLatencies ?? []) is { Median: { } lm } lat ? new(null, lm, null, null, lat.P95) : null,
            EtwEvidence = new(parsed?.LostEvents, parsed?.LostBuffers, parsed?.OverflowedPresents,
                "PresentMon 2.6.0 CSV; final console counters unavailable", false)
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(SqliteBenchmarkRepository.Preview(result)));
        return SqliteBenchmarkRepository.Preview(result);
    }
    public const int FirstFrameSeconds = 10;
    // Tests replace it; otherwise Lotus relaunches itself elevated (UAC) for that one step. 1223 = UAC declined.
    public Func<string[], Task<int>>? ElevatedRunner { get; init; }
    private Task<int> RunElevatedHelper(string[] args) => ElevatedRunner?.Invoke(args) ?? Task.Run(() =>
    {
        if (Environment.ProcessPath is not { } exe || !exe.EndsWith("Lotus.App.exe", StringComparison.OrdinalIgnoreCase)) return 2;
        var info = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var a in args) info.ArgumentList.Add(a);
        try { using var p = Process.Start(info)!; return p.WaitForExit(30000) ? p.ExitCode : 3; }
        catch (System.ComponentModel.Win32Exception) { return 1223; }
    });
    private static Process Start(string[] arguments, bool elevated)
    {
        var info = new ProcessStartInfo(ToolPath) { UseShellExecute = elevated, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = !elevated, RedirectStandardError = !elevated };
        if (elevated) info.Verb = "runas";
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException("Could not start PresentMon.");
    }
    private static async Task Drain(StreamReader reader, StringBuilder destination)
    {
        var buffer = new char[1024]; int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
            lock (destination) { if (destination.Length < 16384) destination.Append(buffer, 0, Math.Min(read, 16384 - destination.Length)); }
    }
}

// ETW sessions Lotus opened and has not seen close (one name per line). Whatever is still listed when the next capture
// starts was left behind by a Lotus that crashed or was closed mid-capture. Only names Lotus created are ever listed.
public sealed class EtwSessionLedger(string root)
{
    private string Path => System.IO.Path.Combine(root, "open-etw-sessions.txt");
    private static readonly System.Text.RegularExpressions.Regex Name = new("^Lotus-[0-9a-f]{32}$");
    public IReadOnlyList<string> Pending()
    {
        try { return File.Exists(Path) ? [.. File.ReadAllLines(Path).Select(l => l.Trim()).Where(l => Name.IsMatch(l)).Distinct()] : []; }
        catch (IOException) { return []; }
    }
    public void Add(string name) => Write([.. Pending().Where(n => n != name), name]);
    public void Remove(string name) => Write([.. Pending().Where(n => n != name)]);
    private void Write(IReadOnlyList<string> names)
    {
        try { Directory.CreateDirectory(root); if (names.Count == 0) File.Delete(Path); else File.WriteAllLines(Path, names); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
