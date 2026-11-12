using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;

namespace Lotus.Infrastructure.Games;

// RivaTuner's frametime pipe (SDK sample RTSSFrametimePipeSample): Lotus opens the server end "\\.\pipe\RTSS_Frametime"
// and RivaTuner's hook inside each game connects and sends one message per presented frame: the app's index in the
// shared memory, then the frametime in microseconds. Every frame arrives, without administrator rights and without
// a trace session. The pipe is opened only for the measurement and only for this Windows user (the SDK sample opens it
// to everyone; Lotus does not).
[SupportedOSPlatform("windows")]
public static class RtssFrametimePipe
{
    public const string DefaultName = "RTSS_Frametime";
    private const int Listeners = 6; // a few hooked apps (launchers, overlays) may connect besides the game

    // Frametimes (ms) of one process for `duration`, counted from its first frame. Null when no frame of that process
    // arrived within `firstFrameWait` (RivaTuner did not connect, or another program owns the pipe).
    public static async Task<IReadOnlyList<double>?> CollectAsync(int pid, TimeSpan duration, TimeSpan firstFrameWait, CancellationToken stop,
        string mapName = RtssSharedMemory.DefaultMap, string pipeName = DefaultName)
    {
        var frames = new List<double>();
        var gate = new object();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owners = new ConcurrentDictionary<uint, int>();
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(stop);

        async Task Listen()
        {
            var buffer = new byte[64];
            while (!listening.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // the name is taken by another program
                await using (pipe)
                {
                    try { await pipe.WaitForConnectionAsync(listening.Token); }
                    catch (OperationCanceledException) { return; }
                    catch (IOException) { continue; }
                    while (!listening.IsCancellationRequested)
                    {
                        int read;
                        try { read = await pipe.ReadAsync(buffer, listening.Token); }
                        catch (OperationCanceledException) { return; }
                        catch (IOException) { break; } // the game closed
                        if (read == 0) break;
                        if (read < 8) continue;
                        var app = BitConverter.ToUInt32(buffer, 0);
                        var microseconds = BitConverter.ToUInt32(buffer, 4);
                        if (microseconds is 0 or > 5_000_000) continue;
                        if (owners.GetOrAdd(app, i => RtssSharedMemory.ProcessAt(i, mapName)) != pid) continue;
                        lock (gate) frames.Add(microseconds / 1000.0);
                        firstFrame.TrySetResult();
                    }
                }
            }
        }

        var listeners = Enumerable.Range(0, Listeners).Select(_ => Task.Run(Listen)).ToArray();
        try
        {
            var started = await Task.WhenAny(firstFrame.Task, Task.Delay(firstFrameWait, stop)) == firstFrame.Task;
            if (!started) return null;
            await Task.Delay(duration, stop);
            lock (gate) return [.. frames];
        }
        finally
        {
            listening.Cancel();
            try { await Task.WhenAll(listeners); } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
        }
    }
}
