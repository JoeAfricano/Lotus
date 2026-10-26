using System.Runtime.InteropServices;

namespace Lotus.Infrastructure.Score;

// While a benchmark runs, Windows must not slow Lotus down for being in the background (EcoQoS / power
// throttling kicks in as soon as another window, like the GPU scene, has focus). Restored when disposed.
public sealed class FullSpeed : IDisposable
{
    private readonly System.Diagnostics.ProcessPriorityClass previous;
    public FullSpeed()
    {
        var p = System.Diagnostics.Process.GetCurrentProcess();
        previous = p.PriorityClass;
        try { p.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal; } catch (System.ComponentModel.Win32Exception) { }
        SetProcess(throttle: false);
    }
    public void Dispose()
    {
        SetProcess(throttle: true);
        try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = previous; } catch (System.ComponentModel.Win32Exception) { }
    }

    // Called on each benchmark thread: opt the thread out of execution-speed throttling too.
    public static void ThisThread()
    {
        var state = new PowerThrottlingState { Version = 1, ControlMask = ExecutionSpeed, StateMask = 0 };
        SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
    }

    private static void SetProcess(bool throttle)
    {
        // Control bit set + state 0 = "never throttle"; control 0 = let Windows decide again.
        var state = new PowerThrottlingState { Version = 1, ControlMask = throttle ? 0u : ExecutionSpeed, StateMask = 0 };
        SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
    }

    private const uint ExecutionSpeed = 0x1;
    private const int ProcessPowerThrottling = 4, ThreadPowerThrottling = 3;
    [StructLayout(LayoutKind.Sequential)] private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref PowerThrottlingState info, int size);
}
