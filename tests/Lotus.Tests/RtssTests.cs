using System.IO.MemoryMappedFiles;
using System.Text;
using Lotus.Core.Benchmark;
using Lotus.Infrastructure.Games;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class RtssTests(ITestOutputHelper output)
{
    // Same layout as the SDK, under a private name: the real RTSS mapping is never touched.
    private readonly string name = "LotusTestRtss_" + Guid.NewGuid().ToString("N");
    private MemoryMappedFile FakeRtss(int pid, uint frames, uint frameTimeUs)
    {
        const int entry = 300, offset = 256;
        var map = MemoryMappedFile.CreateNew(name, offset + entry * 2);
        using var v = map.CreateViewAccessor();
        v.Write(0, 0x52545353u); v.Write(4, 0x00020014u); v.Write(8, (uint)entry); v.Write(12, (uint)offset); v.Write(16, 2u);
        v.Write(offset, pid); v.WriteArray(offset + 4, Encoding.ASCII.GetBytes("game.exe\0"), 0, 9);
        v.Write(offset + 268, 1000u); v.Write(offset + 272, 2000u); v.Write(offset + 276, frames); v.Write(offset + 280, frameTimeUs);
        return map;
    }
    private sealed class Fallback : IFrameCaptureProvider
    {
        public int Calls;
        public Task<BenchmarkSession> CaptureAsync(CaptureRequest r, CancellationToken s) { Calls++; return Task.FromResult(new BenchmarkSession(Guid.NewGuid(), r.Target, DateTimeOffset.UtcNow, 0, "Fallback", "", "", "", "", "", "", null, "")); }
    }

    [Fact] public async Task ReadsAppEntriesAndPrefersRtssOverPresentMon()
    {
        using var map = FakeRtss(4242, 75, 13_333);
        var app = Assert.Single(RtssSharedMemory.Read(name));
        Assert.Equal((4242, "game.exe", 75u, 13_333u), (app.ProcessId, app.Name, app.Frames, app.FrameTimeMicroseconds));
        Assert.True(RtssSharedMemory.IsTracking(4242, name)); Assert.False(RtssSharedMemory.IsTracking(1, name));
        var fallback = new Fallback(); var provider = new RtssPreferredCaptureProvider(fallback, TimeSpan.FromMilliseconds(5), name);
        var session = await provider.CaptureAsync(new(new(4242, "game.exe", DateTimeOffset.UtcNow), 1, false, "", "{}"), CancellationToken.None);
        Assert.Equal(0, fallback.Calls); Assert.Equal("Completed", session.Status);
        Assert.Equal(75, session.Data!.Statistics!.AverageFps, 1); Assert.Equal(75, session.Data.Statistics.Low1Fps, 0);
        await provider.CaptureAsync(new(new(7, "other.exe", DateTimeOffset.UtcNow), 1, true, "", "{}"), CancellationToken.None);
        Assert.Equal(1, fallback.Calls); // not tracked by RTSS → PresentMon
    }

    [Fact] public void RealRtssReadIsSafe()
    {
        var apps = RtssSharedMemory.Read();
        output.WriteLine($"RTSS tracked apps: {apps.Count}" + string.Concat(apps.Select(a => $" | {a.Name} pid {a.ProcessId}")));
        Assert.All(apps, a => Assert.True(a.ProcessId > 0));
    }
}
