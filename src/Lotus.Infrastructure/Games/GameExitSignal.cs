using System.Diagnostics;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// A process-exit notification, not a second polling loop. Callers rescan the library before declaring a game closed:
// launchers may hand off to another process. Start time prevents following an unrelated process with a reused PID.
public static class GameExitSignal
{
    public static async Task<bool> WaitAsync(RunningGame game, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(game.Pid);
            if (process.StartTime.ToUniversalTime() != game.StartedUtc.UtcDateTime) return false;
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return true;
        }
        catch (ArgumentException) { return true; } // exited between discovery and registration: rescan
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; } // keep the existing periodic discovery as fallback
    }
}
