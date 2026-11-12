using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class RtssLatencyTests(ITestOutputHelper output)
{
    private readonly string name = "LotusTestRtss_" + Guid.NewGuid().ToString("N");

    // A v2.21 mapping with two app entries of the real size; entry 1 belongs to `pid` and carries Reflex markers.
    private MemoryMappedFile FakeV221(int pid, ulong input, ulong renderEnd, uint active, uint frame, uint perfOffset = RtssSharedMemory.PerfCountersOffset)
    {
        const int entry = 9600, offset = 256;
        var map = MemoryMappedFile.CreateNew(name, offset + entry * 2);
        using var v = map.CreateViewAccessor();
        v.Write(0, 0x52545353u); v.Write(4, 0x00020015u); v.Write(8, (uint)entry); v.Write(12, (uint)offset); v.Write(16, 2u); v.Write(76, perfOffset);
        v.Write(offset, 1111); v.Write(offset + 268, 1000u);
        long at = offset + entry;
        v.Write(at, pid); v.Write(at + 268, 1000u);
        v.Write(at + RtssSharedMemory.InputSampleOffset, input); v.Write(at + RtssSharedMemory.SimStartOffset, input + 500);
        v.Write(at + RtssSharedMemory.GpuRenderEndOffset, renderEnd); v.Write(at + RtssSharedMemory.GpuActiveOffset, active); v.Write(at + RtssSharedMemory.GpuFrameOffset, frame);
        return map;
    }

    [Fact] public void ReadsReflexMarkersOnlyWithTheExpectedLayout()
    {
        using (FakeV221(4242, 1_000_000, 1_023_500, 9_000, 12_000))
        {
            var f = RtssSharedMemory.ReadReflex(4242, name)!;
            Assert.Equal(23.5, f.PcLatencyMs!.Value, 3); Assert.Equal(75, f.GpuBusyPercent!.Value, 3);
            Assert.Null(RtssSharedMemory.ReadReflex(1111, name)); // no markers = not a Reflex frame
            Assert.Equal(4242, RtssSharedMemory.ProcessAt(1, name)); Assert.Equal(0, RtssSharedMemory.ProcessAt(5, name));
        }
        using (FakeV221(4242, 1_000_000, 1_023_500, 9_000, 12_000, perfOffset: 9352))
            Assert.Null(RtssSharedMemory.ReadReflex(4242, name)); // another layout: refuse instead of reading garbage
    }

    [Fact] public void ReflexFrameRejectsNonsense()
    {
        Assert.Null(new RtssSharedMemory.ReflexFrame(0, 0, 5_000, 1, 2).PcLatencyMs);
        Assert.Null(new RtssSharedMemory.ReflexFrame(5_000, 0, 4_000, 1, 2).PcLatencyMs);         // clock went backwards
        Assert.Null(new RtssSharedMemory.ReflexFrame(1, 0, 2_000_000, 1, 2).PcLatencyMs);         // over a second
        Assert.Equal(1.5, new RtssSharedMemory.ReflexFrame(0, 1000, 2500, 0, 0).PcLatencyMs);      // no input marker: frame start
        Assert.Null(new RtssSharedMemory.ReflexFrame(1, 0, 2, 20_000, 10_000).GpuBusyPercent);    // active > frame
    }

    [Fact] public void StatisticsNeedEnoughFramesAndMatchTheBenchmark()
    {
        Assert.Null(RtssPreferredCaptureProvider.Statistics([.. Enumerable.Repeat(16.0, 59)]));
        Assert.Null(RtssPreferredCaptureProvider.Statistics([.. Enumerable.Repeat(16.0, 100)])); // only 1.6 s
        var frames = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(100.0, 10)).ToArray(); // 10.9 s
        var s = RtssPreferredCaptureProvider.Statistics(frames)!;
        Assert.Equal(1000, s.Count); Assert.Equal(1000 * 1000 / 10_900.0, s.AverageFps, 3);
        Assert.Equal(10, s.Low1Fps, 3); Assert.Equal(10, s.MedianMs); Assert.Equal(10, s.SpikeCount);
    }

    [Theory]
    [InlineData(true, true, false, 75, null, null, "responsive", "LlWhyFrameGen")]
    [InlineData(true, false, false, 75, null, null, null, "LlFrameGenOther")]
    [InlineData(false, true, true, 144, null, null, "responsive", "LlWhyVrrReflex")]
    [InlineData(false, false, true, 144, null, null, "fluid", "LlWhyVrrFluid")]
    [InlineData(false, true, false, 75, 74.0, 60.0, "scanline", "LlWhyScanline")]
    [InlineData(false, true, false, 75, 74.0, 95.0, "fluid", "LlWhyFluid")]
    [InlineData(false, true, false, 75, 95.0, null, "scanline", "LlWhyScanline")]
    [InlineData(false, true, false, 75, 80.0, null, "fluid", "LlWhyFluid")]
    [InlineData(false, true, false, 75, null, null, "fluid", "LlWhyFluidMeasure")]
    public void OneButtonPicksTheMode(bool frameGen, bool nvidia, bool vrr, int hz, double? fps, double? busy, string? mode, string why)
    {
        var info = new GamesViewModel.LowLatencyInfo("ok", "game.exe", null, nvidia, FrameGen: frameGen, Api: "Direct3D 12");
        Assert.Equal((mode, why), LimiterChoice.Pick(info, vrr, hz, fps, busy));
        Assert.Equal((null, "LlFrameGenOther"), LimiterChoice.Pick(info with { FrameGen = true, Api = "Vulkan" }, vrr, hz, fps, busy)); // Reflex injection is D3D only
    }

    [Fact] public async Task PipeCollectsOnlyTheGamesFrames()
    {
        using var map = FakeV221(4242, 1, 2, 1, 2);
        var pipe = "LotusTestPipe_" + Guid.NewGuid().ToString("N");
        var collect = RtssFrametimePipe.CollectAsync(4242, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(3), CancellationToken.None, name, pipe);
        async Task Send(uint app, uint us, int count)
        {
            using var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await c.ConnectAsync(3000); c.ReadMode = PipeTransmissionMode.Message;
            var msg = new byte[8];
            for (var i = 0; i < count; i++) { BitConverter.TryWriteBytes(msg.AsSpan(0), app); BitConverter.TryWriteBytes(msg.AsSpan(4), us); await c.WriteAsync(msg); await c.FlushAsync(); }
            await Task.Delay(100);
        }
        await Task.WhenAll(Send(1, 16_667, 30), Send(0, 5_000, 30)); // entry 0 is another app
        var frames = await collect;
        Assert.NotNull(frames); Assert.Equal(30, frames!.Count); Assert.All(frames, f => Assert.Equal(16.667, f, 3));
        Assert.Null(await RtssFrametimePipe.CollectAsync(4242, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(150), CancellationToken.None, name, pipe));
    }

    // Live check on the PC: LOTUS_LIVE=1 and LOTUS_LIVE_PID=<pid of a 3D app RTSS hooks>, with RivaTuner open.
    [Fact] public async Task LiveCapture()
    {
        if (Environment.GetEnvironmentVariable("LOTUS_LIVE") != "1" || !int.TryParse(Environment.GetEnvironmentVariable("LOTUS_LIVE_PID"), out var pid)) return;
        output.WriteLine($"tracked: {RtssSharedMemory.IsTracking(pid)}; reflex now: {RtssSharedMemory.ReadReflex(pid)}");
        var pipe = await RtssFrametimePipe.CollectAsync(pid, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(5), CancellationToken.None);
        output.WriteLine(pipe is null ? "pipe: no frames" : $"pipe: {pipe.Count} frames, stats {RtssPreferredCaptureProvider.Statistics(pipe)}");
        var provider = new RtssPreferredCaptureProvider(new NoFallback());
        var session = await provider.CaptureAsync(new(new(pid, "live", DateTimeOffset.UtcNow), 8, false, "", "{}"), CancellationToken.None);
        output.WriteLine($"session: {session.Status} {session.AlgorithmVersion} {session.Data?.Statistics} extras {session.Extras}");
    }
    private sealed class NoFallback : Lotus.Core.Benchmark.IFrameCaptureProvider
    {
        public Task<Lotus.Core.Benchmark.BenchmarkSession> CaptureAsync(Lotus.Core.Benchmark.CaptureRequest r, CancellationToken s) => throw new InvalidOperationException("RTSS is not tracking the process");
    }
}
