using System.Diagnostics;
using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class GameExitSignalTests
{
    private static RunningGame Identity(Process process) => new(process.Id,process.ProcessName,new DateTimeOffset(process.StartTime.ToUniversalTime()));

    [Fact] public async Task Cancellation_releases_the_wait_without_stopping_the_process()
    {
        using var process=Process.GetCurrentProcess();
        using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>GameExitSignal.WaitAsync(Identity(process),stop.Token));
        Assert.False(process.HasExited);
    }

    [Fact] public async Task Reused_pid_with_another_start_time_is_not_followed()
    {
        using var process=Process.GetCurrentProcess();
        var different=Identity(process) with { StartedUtc=DateTimeOffset.MinValue };
        Assert.False(await GameExitSignal.WaitAsync(different,CancellationToken.None));
        Assert.False(process.HasExited);
    }

    [Fact] public async Task Real_process_exit_requests_a_rescan_without_waiting_for_the_periodic_poll()
    {
        if(!OperatingSystem.IsWindows())return;
        var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute=false,CreateNoWindow=true };
        info.ArgumentList.Add("-NoProfile");info.ArgumentList.Add("-NonInteractive");info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Start-Sleep -Milliseconds 600");
        using var process=Process.Start(info)!;
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var exited=await GameExitSignal.WaitAsync(Identity(process),stop.Token);
        Assert.True(exited); Assert.True(process.HasExited);
    }
}
