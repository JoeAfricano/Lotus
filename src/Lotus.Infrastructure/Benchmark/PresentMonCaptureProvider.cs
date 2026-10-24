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
            var sessionName = "Lotus-" + id.ToString("N");
            var args = new[] { "--process_id", request.Target.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--output_file", raw, "--session_name", sessionName, "--timed", request.Seconds.ToString(),
                "--terminate_after_timed", "--no_console_stats", "--no_track_input", "--no_track_gpu", "--track_frame_type" };
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
            var stopped = false; var targetExited = false; var limitExceeded = false;
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
                if (!stopped && (stop.IsCancellationRequested || targetExited || limitExceeded || collectionClock.Elapsed.TotalSeconds > request.Seconds + 15))
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
            diagnostics.AppendLine($"ExitCode={collector.ExitCode}; StopRequested={stop.IsCancellationRequested}; TargetExited={targetExited}; SizeLimit={limitExceeded}");
            diagnostics.AppendLine("ETW loss counters unavailable in validated 2.6.0 CSV. Results are conditional on capture integrity.");
            if (request.Elevated) diagnostics.AppendLine("Elevated collector: console diagnostics unavailable.");
            if (File.Exists(raw) && new FileInfo(raw).Length > 0)
            {
                using var stream = File.OpenRead(raw); hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                parsed = PresentMonCsv.Read(raw, request.Target.Pid);
            }
            diagnostics.AppendLine($"ETW: events={parsed?.LostEvents?.ToString() ?? "unavailable"}; buffers={parsed?.LostBuffers?.ToString() ?? "unavailable"}; overflow={parsed?.OverflowedPresents?.ToString() ?? "unavailable"}");
            var reportedLoss = parsed?.LostEvents > 0 || parsed?.LostBuffers > 0 || parsed?.OverflowedPresents > 0;
            status = limitExceeded || parsed?.Truncated == true ? "LimitExceeded" : collector.ExitCode != 0 ? "Failed" :
                reportedLoss ? "EventLoss" : parsed?.Statistics is not null ? "Completed" : "InsufficientData";
            if (status != "Completed" && parsed is not null) parsed = parsed with { Statistics = null };
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
            raw, hash, request.Conditions, request.HardwareJson, parsed, diagnostics.ToString()) { ResourceUsage = usage };
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(SqliteBenchmarkRepository.Preview(result)));
        return SqliteBenchmarkRepository.Preview(result);
    }
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
