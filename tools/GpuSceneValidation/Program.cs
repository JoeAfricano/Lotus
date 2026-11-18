using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lotus.Infrastructure.Score;
using Lotus.Core;
using Lotus.Core.Score;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

// Bounded graphics verification: three offscreen frames, no clock/profile changes, no stress loop.
if (!OperatingSystem.IsWindowsVersionAtLeast(6,1)) return 1;
if (args.Contains("--local-preview-latency"))
{
    var source = Enumerable.Range(0, 800 * 450 * 4).Select(i => (byte)(i % 4 == 3 ? 255 : i % 256)).ToArray();
    var baseline = new Lotus.Core.Display.CalibrationState("synthetic", "synthetic", "Synthetic preview input", false,
        new(65, 0, 100), new(60, 0, 100), null, new(50, 0, 100, 50));
    var profile = new Lotus.Core.Display.CalibrationProfile(57, 65, 1.04, Warmth: 5, Saturation: 55);
    var times = new List<double>(); byte[]? output = null;
    for (var i = 0; i < 14; i++)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        output = Lotus.Core.Display.DisplayImagePreview.Render(source, profile, baseline);
        timer.Stop(); if (i >= 2) times.Add(timer.Elapsed.TotalMilliseconds);
    }
    times.Sort();
    var result = new { Width = 800, Height = 450, InputSynthetic = true, HardwareSettingsChanged = false,
        Samples = times.Count, MedianMilliseconds = times[times.Count / 2], MaximumMilliseconds = times[^1],
        OutputSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(output!)),
        Scope = "CPU transform only; excludes UI scheduling, bitmap upload and monitor response" };
    File.WriteAllText(Path.GetFullPath(args.Last()), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(result)); return 0;
}
// Requires the player's explicit confirmation; a fixed, bounded physical-monitor smoke test.
if (args.Contains("--calibration-smoke-approved"))
{
    if (System.Diagnostics.Process.GetProcessesByName("Lotus.App").Length != 0) throw new InvalidOperationException("Close Lotus before this isolated test.");
    var driver = new Lotus.Infrastructure.Display.WindowsDisplayCalibration();
    var before = driver.Displays().Single(s => s.CanAdjust);
    if (before.Brightness is null || before.Contrast is null || before.Ramp is null) throw new InvalidOperationException("Expected brightness, contrast and SDR curve support.");
    if (before.Brightness.Current != 65 || before.Contrast.Current != 60) throw new InvalidOperationException("Monitor values changed since authorization; no write performed.");
    var recovery = new Lotus.Infrastructure.Display.JsonCalibrationStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "monitor-calibration.json"));
    if (recovery.Load().Count != 0) throw new InvalidOperationException("Existing user calibration must be preserved.");
    using var session = new Lotus.Core.Display.DisplayCalibrationController(driver, recovery);
    Lotus.Core.Display.MonitorColorResult applied = default, undone = default;
    Lotus.Core.Display.CalibrationState? actual = null;
    try
    {
        applied = session.Begin(before, new(66, 61, 1.03)); actual = driver.Read(before.Identity);
        if (applied == Lotus.Core.Display.MonitorColorResult.Preview) await Task.Delay(TimeSpan.FromSeconds(3));
    }
    finally { undone = session.CancelPreview(); }
    var after = driver.Read(before.Identity);
    var success = applied == Lotus.Core.Display.MonitorColorResult.Preview && actual?.Brightness?.Current == 66 && actual.Contrast?.Current == 61 &&
        !Lotus.Core.Display.CalibrationState.RampEqual(before.Ramp, actual.Ramp, 256) && after is not null && after.Same(before, 256) &&
        undone == Lotus.Core.Display.MonitorColorResult.Restored && recovery.Load().Count == 0;
    var evidence = new { Success = success, HardwareSettingsChangedTemporarily = true, Applied = applied.ToString(), Restored = undone.ToString(),
        BeforeBrightness = before.Brightness.Current, BeforeContrast = before.Contrast.Current, AppliedBrightness = actual?.Brightness?.Current,
        AppliedContrast = actual?.Contrast?.Current, AfterBrightness = after?.Brightness?.Current, AfterContrast = after?.Contrast?.Current,
        GammaReadbackChanged = actual is not null && !Lotus.Core.Display.CalibrationState.RampEqual(before.Ramp, actual.Ramp, 256),
        OriginalVerified = after?.Same(before, 256), GammaTolerance16Bit = 256, OpticalAccuracyMeasured = false };
    File.WriteAllText(Path.GetFullPath(args.Last()), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(evidence)); return success ? 0 : 1;
}
if (args.Contains("--display-calibration"))
{
    var values = new Lotus.Infrastructure.Display.WindowsDisplayCalibration().Displays();
    Console.WriteLine(JsonSerializer.Serialize(new { Displays = values.Select(v => new { v.Identity, v.Device, v.Name,
        v.AdvancedColor, v.Brightness, v.Contrast, GammaAvailable = v.Ramp is not null, v.CanAdjust }), HardwareSettingsChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    return values.Count > 0 ? 0 : 1;
}
if (args.Contains("--monitor-colors"))
{
    var colors = new Lotus.Infrastructure.Display.WindowsMonitorColors().Displays();
    Console.WriteLine(JsonSerializer.Serialize(new { Displays = colors, HardwareSettingsChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    return colors.Count > 0 ? 0 : 1;
}
if(args.Contains("--display-modes"))
{
    var display=new Lotus.Infrastructure.Games.WindowsGameDisplay();
    var modes=display.Modes();
    var current=modes.DistinctBy(m=>m.Device).Select(m=>display.Current(m.Device)).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new {Current=current,Modes=modes,HardwareSettingsChanged=false},new JsonSerializerOptions {WriteIndented=true}));
    return current.Length>0?0:1;
}
if (args.Any(a => a.StartsWith("--calibrate-", StringComparison.Ordinal) || a == "--validate-score") && !new CpuRayBenchmark().IsOptimized)
    throw new InvalidOperationException("Benchmark calibration and numerical validation require optimized Release binaries.");
if (args.Contains("--validate-score"))
{
    var history = new ValidationHistory(); var scoreGpu = new D3D11GpuBenchmark(); var errors = new List<string>();
    var vm = new LotusScoreViewModel(new CpuRayBenchmark(), scoreGpu, history, new WindowsScoreMachine(scoreGpu), new TextCatalog(), ex => errors.Add(ex.ToString()));
    vm.Refresh(); await vm.ToggleAsync();
    var run = history.Load().SingleOrDefault();
    var valid = run is { Cpu1: > 0, CpuAll: > 0, Gpu: > 0, Total: > 0, Evidence: not null } && errors.Count == 0;
    File.WriteAllText(Path.GetFullPath(args.Last()), JsonSerializer.Serialize(new { Valid = valid, Run = run, vm.Verdict, vm.Reliability, Errors = errors, HardwareSettingsChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Actual Score ViewModel integration completed: {valid}; {vm.Cpu1}/{vm.CpuAll}/{vm.Gpu}; total {vm.Total}. User history untouched.");
    return valid ? 0 : 1;
}
if(args.Contains("--calibrate-gpu"))
{
    var calibrationGpu=new D3D11GpuBenchmark();
    var passes=calibrationGpu.Run(3,TimeSpan.FromSeconds(3),_=>{},CancellationToken.None);
    File.WriteAllText(Path.GetFullPath(args.Last()),JsonSerializer.Serialize(new{Adapter=calibrationGpu.Adapter(),Passes=passes,LotusScale.WorkloadRevision,LotusScale.Calibration,Build="Release",HardwareSettingsChanged=false}));
    return 0;
}
if(args.Contains("--calibrate-flower"))
{
    var output=Path.GetFullPath(args.Last()); Directory.CreateDirectory(output);
    var cpu=new CpuRayBenchmark();
    double[] Measure(int threads)=>Enumerable.Range(0,4).Select(_=>cpu.Run(threads,TimeSpan.FromSeconds(3),CancellationToken.None)).Skip(1).ToArray();
    var single=Measure(1); var multi=Measure(Environment.ProcessorCount);
    var gpuTest=new D3D11GpuBenchmark();
    var graphics=gpuTest.Run(3,TimeSpan.FromSeconds(3),_=>{},CancellationToken.None);
    var machine=new WindowsScoreMachine(gpuTest);
    File.WriteAllText(Path.Combine(output,"calibration.json"),JsonSerializer.Serialize(new{Cpu=machine.Cpu(),Gpu=machine.Gpu(),Single=single,Multi=multi,Graphics=graphics,LotusScale.WorkloadRevision,LotusScale.Calibration,Build="Release",Runtime=RuntimeInformation.FrameworkDescription,Threads=Environment.ProcessorCount,HardwareSettingsChanged=false}));
    using var image=new Bitmap(320,180);
    for(var y=0;y<180;y++)for(var x=0;x<320;x++)
    {
        var c=CpuRayBenchmark.Pixel(x,y);
        int Channel(float v)=>(int)Math.Clamp(MathF.Sqrt(Math.Max(0,v))*255,0,255);
        image.SetPixel(x,y,Color.FromArgb(Channel(c.X),Channel(c.Y),Channel(c.Z)));
    }
    image.Save(Path.Combine(output,"cpu-flower.png"),ImageFormat.Png);
    Console.WriteLine("Real floral CPU/GPU calibration completed; no clock/profile changes.");
    return 0;
}
if (args.Contains("--window-preview") || args.Contains("--window-controls"))
{
    var controls=args.Contains("--window-controls");
    var settings=new Lotus.Core.Benchmark.OverlayWindowSettings {Position="top-right"};
    var toggles=0; var moves=0;
    // Mirror the app's active-style check; do not force a second renderer above an active native RTSS layout.
    // Explicit controls diagnostic tests only this renderer; any native RTSS overlay remains externally managed.
    using var overlay = controls || Lotus.Infrastructure.Integrations.LotusOverlayLayout.WindowActive()
        ? new Lotus.Infrastructure.Games.GameOverlayWindow(Environment.ProcessId, EmptySensors, ()=>WindowLook() with {Settings=Volatile.Read(ref settings)},
            ()=>{Volatile.Write(ref settings,settings.NextPosition()); Interlocked.Increment(ref moves);},
            ()=>{Volatile.Write(ref settings,settings with {Visible=!settings.Visible}); Interlocked.Increment(ref toggles);}) : null;
    var ok = D3D11GpuBenchmark.Preview();
    Console.WriteLine(JsonSerializer.Serialize(new {PreviewCompleted=ok,Renders=overlay?.Renders,ReceivingFrames=overlay?.ReceivingFrames,ControlsDiagnostic=controls,Toggles=toggles,Moves=moves,Visible=settings.Visible,HardwareSettingsChanged=false}));
    return ok ? 0 : 2;
}
var folder = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/scene-validation");
Directory.CreateDirectory(folder);
var shader = (string)typeof(D3D11GpuBenchmark).GetField("TuningShader", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
factory.EnumAdapterByGpuPreference(0, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).CheckError();
using var gpu = adapter!;
D3D11.D3D11CreateDevice(gpu, DriverType.Unknown, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
using var dev = device!; using var ctx = context!;
using var vs = dev.CreateVertexShader(Compiler.Compile(shader,"VS","lotus-scene.hlsl","vs_5_0").Span);
using var ps = dev.CreatePixelShader(Compiler.Compile(shader,"PS","lotus-scene.hlsl","ps_5_0").Span);
using var cb = dev.CreateBuffer(new BufferDescription(16,BindFlags.ConstantBuffer));
var desc = new Texture2DDescription { Width=1920, Height=1080, MipLevels=1, ArraySize=1, Format=Format.B8G8R8A8_UNorm, SampleDescription=new(1,0), Usage=ResourceUsage.Default, BindFlags=BindFlags.RenderTarget };
using var target = dev.CreateTexture2D(desc);
desc.Usage=ResourceUsage.Staging; desc.BindFlags=BindFlags.None; desc.CPUAccessFlags=CpuAccessFlags.Read;
using var staging = dev.CreateTexture2D(desc);
using var rtv = dev.CreateRenderTargetView(target);
ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList); ctx.VSSetShader(vs); ctx.PSSetShader(ps); ctx.PSSetConstantBuffer(0,cb);
foreach(var time in new[]{0f,8f,16f})
{
    ctx.OMSetRenderTargets(rtv); ctx.RSSetViewport(0,0,1920,1080); ctx.UpdateSubresource(new Vector4(time,0,0,0),cb); ctx.Draw(3,0);
    ctx.CopyResource(staging,target);
    var mapped=ctx.Map(staging,0,MapMode.Read);
    try
    {
        using var bmp=new Bitmap(1920,1080,PixelFormat.Format32bppArgb);
        var bits=bmp.LockBits(new Rectangle(0,0,1920,1080),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try { var row=new byte[1920*4]; for(var y=0;y<1080;y++){Marshal.Copy(mapped.DataPointer+(int)(y*mapped.RowPitch),row,0,row.Length);Marshal.Copy(row,0,bits.Scan0+y*bits.Stride,row.Length);} }
        finally {bmp.UnlockBits(bits);}
        bmp.Save(Path.Combine(folder,$"lotus-scene-{time:00}.png"),ImageFormat.Png);
    }
    finally {ctx.Unmap(staging,0);}
}
File.WriteAllText(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(new {ShaderCompiled=true,OffscreenFrames=3,Width=1920,Height=1080,Adapter=gpu.Description1.Description,HardwareSettingsChanged=false}));
Console.WriteLine("Shader compilado; três quadros reais renderizados em 1920x1080. Nenhum ajuste de hardware alterado.");
return 0;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static Lotus.Infrastructure.Games.GameOverlayWindow.Sensors EmptySensors() => new(null,null,null,null,null,null);
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static Lotus.Infrastructure.Games.GameOverlayWindow.Look WindowLook() => new(Color.FromArgb(242,123,168),Color.FromArgb(238,193,168),null,"Média","—",new Lotus.Core.Benchmark.OverlayWindowSettings {Position="top-right"});

sealed class ValidationHistory : IScoreHistory
{
    private IReadOnlyList<ScoreRun> runs = [];
    public IReadOnlyList<ScoreRun> Load() => runs;
    public void Save(IReadOnlyList<ScoreRun> value) => runs = value;
}
