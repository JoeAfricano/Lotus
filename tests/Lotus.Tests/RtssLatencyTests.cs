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
    [InlineData(true, true, true, 144, null, null, "responsive", "LlWhyFrameGen")]
    [InlineData(true, false, true, 144, null, null, null, "LlFrameGenOther")]
    [InlineData(true, true, false, 75, null, null, "fluid", "LlWhyFluidMeasure")]   // fixed 75 Hz: frame generation should be off, normal pick
    [InlineData(true, true, true, 75, null, null, "responsive", "LlWhyVrrReflex")]   // VRR but slow monitor: same
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
        if (frameGen && vrr && hz >= 120) Assert.Equal((null, "LlFrameGenOther"), LimiterChoice.Pick(info with { Api = "Vulkan" }, vrr, hz, fps, busy)); // Reflex injection is D3D only
    }

    [Fact]
    public void FrameGenIsThePlayersChoice()
    {
        var info = new GamesViewModel.LowLatencyInfo("ok", "game.exe", null, true, FrameGen: true, Api: "Direct3D 12", UsesFrameGen: true);
        Assert.Equal(("responsive", "LlWhyFrameGenChosen"), LimiterChoice.Pick(info, false, 75, 74, 60));  // fixed 75 Hz, chosen anyway
        Assert.Equal(("responsive", "LlWhyFrameGen"), LimiterChoice.Pick(info, true, 144, null, null));   // where it fits
        Assert.Equal((null, "LlFrameGenOther"), LimiterChoice.Pick(info with { Nvidia = false }, false, 75, null, null));
        Assert.Equal(("scanline", "LlWhyScanline"), LimiterChoice.Pick(info with { UsesFrameGen = false }, false, 75, 74, 60)); // not chosen: normal pick
    }

    [Fact] public async Task PipeKeepsOnlyTheMeasuredGamesFrames()
    {
        using var map = FakeV221(4242, 1, 2, 1, 2);
        var name2 = "LotusTestPipe_" + Guid.NewGuid().ToString("N");
        using var listener = new RtssFrametimeListener(name, name2);
        Assert.Null(await listener.CollectAsync(4242, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), CancellationToken.None)); // not started
        listener.Start();
        // The games connect when they open (before any measurement), like RivaTuner's hook.
        async Task<NamedPipeClientStream> Connect() { var c = new NamedPipeClientStream(".", name2, PipeDirection.InOut, PipeOptions.Asynchronous); await c.ConnectAsync(3000); c.ReadMode = PipeTransmissionMode.Message; return c; }
        using var game = await Connect(); using var other = await Connect();
        async Task Send(NamedPipeClientStream c, uint app, uint us, int count)
        {
            var msg = new byte[8];
            for (var i = 0; i < count; i++) { BitConverter.TryWriteBytes(msg.AsSpan(0), app); BitConverter.TryWriteBytes(msg.AsSpan(4), us); await c.WriteAsync(msg); await c.FlushAsync(); }
        }
        await Send(game, 1, 33_000, 10); // before the measurement: not kept
        await Task.Delay(100);
        var collect = listener.CollectAsync(4242, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(3), CancellationToken.None);
        await Task.Delay(100);
        await Task.WhenAll(Send(game, 1, 16_667, 30), Send(other, 0, 5_000, 30)); // entry 0 is another app
        var frames = await collect;
        Assert.NotNull(frames); Assert.Equal(30, frames!.Count); Assert.All(frames, f => Assert.Equal(16.667, f, 3));
        Assert.Equal(2, listener.Connected);
        Assert.Null(await listener.CollectAsync(4242, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(150), CancellationToken.None)); // no frames now
    }

    // Live check on the PC, RivaTuner open. LOTUS_LIVE=1 and either LOTUS_LIVE_EXE=<3D program to start> (optional
    // LOTUS_LIVE_ARGS) or LOTUS_LIVE_PROCESS=<process name to wait for, up to 3 min; open the game after the test starts>.
    // The pipe is opened first, as Lotus does at startup. LOTUS_LIVE_SECONDS sets the measurement (default 10).
    [Fact] public async Task LiveCapture()
    {
        if (Environment.GetEnvironmentVariable("LOTUS_LIVE") != "1") return;
        using var listener = new RtssFrametimeListener();
        listener.Start();
        System.Diagnostics.Process? started = null, target = null;
        if (Environment.GetEnvironmentVariable("LOTUS_LIVE_EXE") is { Length: > 0 } exe)
            target = started = System.Diagnostics.Process.Start(exe, Environment.GetEnvironmentVariable("LOTUS_LIVE_ARGS") ?? "");
        else if (Environment.GetEnvironmentVariable("LOTUS_LIVE_PROCESS") is { Length: > 0 } wanted)
            for (var i = 0; i < 360 && target is null; i++) { target = System.Diagnostics.Process.GetProcessesByName(wanted).FirstOrDefault(); if (target is null) await Task.Delay(500); }
        if (target is null) { output.WriteLine("no target process"); return; }
        try
        {
            // RivaTuner hooks it and the hook connects; LOTUS_LIVE_DELAY waits longer (a benchmark's menu before the run).
            await Task.Delay(TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("LOTUS_LIVE_DELAY"), out var d) ? d : 8));
            var pid = target.Id;
            var seconds = int.TryParse(Environment.GetEnvironmentVariable("LOTUS_LIVE_SECONDS"), out var s) ? s : 10;
            output.WriteLine($"pid {pid}; tracked: {RtssSharedMemory.IsTracking(pid)}; pipe clients: {listener.Connected}; reflex now: {RtssSharedMemory.ReadReflex(pid)}");
            var provider = new RtssPreferredCaptureProvider(new NoFallback(), listener: listener);
            var session = await provider.CaptureAsync(new(new(pid, "live", DateTimeOffset.UtcNow), seconds, false, "", "{}"), CancellationToken.None);
            output.WriteLine($"session: {session.Status} {session.AlgorithmVersion} {session.Data?.Statistics} extras {session.Extras}");
        }
        finally { if (started is { HasExited: false }) started.Kill(); }
    }
    private sealed class NoFallback : Lotus.Core.Benchmark.IFrameCaptureProvider
    {
        public Task<Lotus.Core.Benchmark.BenchmarkSession> CaptureAsync(Lotus.Core.Benchmark.CaptureRequest r, CancellationToken s) => throw new InvalidOperationException("RTSS is not tracking the process");
    }
}
