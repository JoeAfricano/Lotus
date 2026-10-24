using System.Diagnostics;
using Lotus.Core.Benchmark;

namespace Lotus.Infrastructure.Benchmark;

public sealed class WindowsCaptureTargets : ICaptureTargetSource
{
    public Task<IReadOnlyList<CaptureTarget>> ListAsync() => Task.Run<IReadOnlyList<CaptureTarget>>(() =>
    {
        var result = new List<CaptureTarget>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                    var path = process.MainModule?.FileName;
                    if (path is not null) result.Add(new(process.Id, path, process.StartTime.ToUniversalTime()));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return result.OrderBy(t => t.Label).ToArray();
    });
}
