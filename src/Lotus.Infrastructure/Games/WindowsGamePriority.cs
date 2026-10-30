using System.Diagnostics;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Process priorities for the player's own apps (no elevation needed). Each change is remembered with the process start
// time, so a reused process id is never touched, and only put back if nobody else changed it meanwhile.
[SupportedOSPlatform("windows")]
public sealed class WindowsGamePriority : IGamePriority
{
    private sealed record Change(int Pid, DateTime Started, ProcessPriorityClass Original, ProcessPriorityClass Set);
    private readonly Dictionary<int, Change> changes = [];
    private readonly int session = Process.GetCurrentProcess().SessionId;
    private readonly object gate = new();

    public int Apply(RunningGame game)
    {
        var count = 0;
        lock (gate)
        {
            if (!changes.ContainsKey(game.Pid) && TrySet(game.Pid, ProcessPriorityClass.Normal, ProcessPriorityClass.AboveNormal)) count++;
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == game.Pid || p.SessionId != session || changes.ContainsKey(p.Id) || !GamePriorityRules.IsBackground(p.ProcessName)) continue;
                        if (TrySet(p.Id, ProcessPriorityClass.Normal, ProcessPriorityClass.BelowNormal)) count++;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
        }
        return count;
    }

    public int Restore()
    {
        var count = 0;
        lock (gate)
        {
            foreach (var c in changes.Values)
            {
                try
                {
                    using var p = Process.GetProcessById(c.Pid);
                    if (p.StartTime != c.Started || p.PriorityClass != c.Set) continue; // gone, reused or changed by someone else
                    p.PriorityClass = c.Original; count++;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            changes.Clear();
        }
        return count;
    }

    // Only from the expected current priority: anything the player set by hand is left alone.
    private bool TrySet(int pid, ProcessPriorityClass expected, ProcessPriorityClass target)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.PriorityClass != expected) return false;
            var started = p.StartTime;
            p.PriorityClass = target;
            changes[pid] = new(pid, started, expected, target);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
}
