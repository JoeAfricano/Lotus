using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;

namespace Lotus.Infrastructure.Games;

// RivaTuner's frametime pipe (SDK sample RTSSFrametimePipeSample, FRAMETIME_PIPE_DATA { DWORD dwApp; DWORD dwFrametime; }):
// Lotus owns the server end "\\.\pipe\RTSS_Frametime" and RivaTuner's hook inside each game connects as a client and
// sends one 8-byte message per presented frame: the app's index in the shared memory, then the frametime in µs.
// Every frame arrives, without administrator rights and without a trace session.
// The hook connects only while the game starts (it retries a few times, then gives up), so the pipe must already be
// open when the game opens: Lotus listens from its own start until it closes. Idle it costs nothing (an async wait);
// with a game open it reads 8 bytes per frame and keeps them only while a measurement asks for that process. The pipe
// is open only to this Windows user (the SDK sample opens it to everyone; Lotus does not).
[SupportedOSPlatform("windows")]
public sealed class RtssFrametimeListener(string mapName = RtssSharedMemory.DefaultMap, string pipeName = RtssFrametimeListener.DefaultName) : IDisposable
{
    public const string DefaultName = "RTSS_Frametime";
    private const int Listeners = 4; // a few hooked apps (launchers, Lotus itself) may connect besides the game
    public static RtssFrametimeListener Shared { get; } = new();

    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<uint, (int Pid, long At)> owners = new();
    private readonly ConcurrentDictionary<int, Collector> collectors = new();
    private int started;

    // Games connected now (for diagnostics) and whether the pipe name was taken by another program.
    public int Connected => connected;
    public bool Blocked { get; private set; }
    public bool Listening => started == 1 && !Blocked && !stopping.IsCancellationRequested;
    private int connected;

    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 1) return;
        for (var i = 0; i < Listeners; i++) _ = Task.Run(Listen);
    }

    public void Dispose() { stopping.Cancel(); }

    private sealed class Collector
    {
        public readonly List<double> Frames = [];
        public readonly TaskCompletionSource First = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Frametimes (ms) of one process for `duration`, counted from its first frame. Null when no frame of that process
    // arrived within `firstFrameWait` (the game opened before Lotus, or RivaTuner is closed).
    public async Task<IReadOnlyList<double>?> CollectAsync(int pid, TimeSpan duration, TimeSpan firstFrameWait, CancellationToken stop)
    {
        if (!Listening) return null;
        var collector = new Collector();
        if (!collectors.TryAdd(pid, collector)) return null; // one measurement per game at a time
        try
        {
            var first = await Task.WhenAny(collector.First.Task, Task.Delay(firstFrameWait, stop)) == collector.First.Task;
            if (!first) return null;
            await Task.Delay(duration, stop);
            lock (collector.Frames) return [.. collector.Frames];
        }
        finally { collectors.TryRemove(pid, out _); }
    }

    private int OwnerOf(uint app)
    {
        var now = Environment.TickCount64;
        if (owners.TryGetValue(app, out var o) && now - o.At < 2000) return o.Pid; // indexes are reused after a game closes
        var pid = RtssSharedMemory.ProcessAt(app, mapName);
        owners[app] = (pid, now);
        return pid;
    }

    private async Task Listen()
    {
        var buffer = new byte[64];
        var token = stopping.Token;
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Blocked = true; return; } // the name is taken by another program
            await using (pipe)
            {
                try { await pipe.WaitForConnectionAsync(token); }
                catch (OperationCanceledException) { return; }
                catch (IOException) { continue; }
                Interlocked.Increment(ref connected);
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        int read;
                        try { read = await pipe.ReadAsync(buffer, token); }
                        catch (OperationCanceledException) { return; }
                        catch (IOException) { break; } // the game closed
                        if (read == 0) break;
                        if (read < 8 || collectors.IsEmpty) continue;
                        var microseconds = BitConverter.ToUInt32(buffer, 4);
                        if (microseconds is 0 or > 5_000_000) continue;
                        if (!collectors.TryGetValue(OwnerOf(BitConverter.ToUInt32(buffer, 0)), out var c)) continue;
                        lock (c.Frames) c.Frames.Add(microseconds / 1000.0);
                        c.First.TrySetResult();
                    }
                }
                finally { Interlocked.Decrement(ref connected); }
            }
        }
    }
}
