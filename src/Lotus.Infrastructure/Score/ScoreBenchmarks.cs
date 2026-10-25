using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Lotus.Core.Score;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Lotus.Infrastructure.Score;

// CPU test: a small deterministic ray tracer (spheres, shadows, reflections). Each thread renders whole
// rows of the same scene; the result is rendered pixels per second / 100.
public sealed class CpuRayBenchmark : ICpuBenchmark
{
    private const int Width = 320, Height = 180;
    private static readonly (Vector3 C, float R, Vector3 Color, float Mirror)[] Spheres =
    [
        (new(0, -1000, 0), 999f, new(0.75f, 0.7f, 0.72f), 0.1f),
        (new(-1.6f, 0, 4), 1f, new(0.95f, 0.45f, 0.65f), 0.4f),
        (new(0.6f, -0.3f, 3.2f), 0.7f, new(0.93f, 0.76f, 0.66f), 0.6f),
        (new(2f, 0.2f, 5f), 1.2f, new(0.55f, 0.3f, 0.45f), 0.2f),
        (new(-0.4f, -0.6f, 2.2f), 0.4f, new(0.9f, 0.9f, 0.95f), 0.8f),
    ];
    private static readonly Vector3 Light = Vector3.Normalize(new(-0.6f, 1f, -0.4f));

    public double Run(int threads, TimeSpan duration, CancellationToken ct)
    {
        threads = Math.Max(1, threads);
        long pixels = 0; var nextRow = -1L; var stop = false;
        var sw = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            long local = 0; var sink = 0f;
            while (!Volatile.Read(ref stop))
            {
                var row = (int)(Interlocked.Increment(ref nextRow) % Height);
                for (var x = 0; x < Width; x++) sink += Pixel(x, row).X;
                local += Width;
            }
            Interlocked.Add(ref pixels, local + (sink < -1 ? 1 : 0));
        }) { IsBackground = true, Priority = ThreadPriority.Normal }).ToArray();
        foreach (var w in workers) w.Start();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested) Thread.Sleep(10);
        Volatile.Write(ref stop, true);
        foreach (var w in workers) w.Join();
        ct.ThrowIfCancellationRequested();
        return pixels / sw.Elapsed.TotalSeconds / 100.0;
    }

    public static Vector3 Pixel(int x, int y)
    {
        var color = Vector3.Zero;
        for (var s = 0; s < 4; s++) // 2x2 supersampling
        {
            var u = (x + (s & 1) * 0.5f) / Width * 2 - 1; var v = 1 - (y + (s >> 1) * 0.5f) / Height * 2;
            color += Trace(new(0, 0.3f, -1), Vector3.Normalize(new(u * 1.78f, v, 1.6f)), 0);
        }
        return color / 4;
    }

    private static Vector3 Trace(Vector3 origin, Vector3 dir, int depth)
    {
        var (hit, t) = Hit(origin, dir);
        if (hit < 0) return Vector3.Lerp(new(0.2f, 0.12f, 0.18f), new(0.55f, 0.4f, 0.5f), Math.Max(0, dir.Y));
        var s = Spheres[hit]; var p = origin + dir * t; var n = Vector3.Normalize(p - s.C);
        var shadow = Hit(p + n * 1e-3f, Light).Index >= 0 ? 0.25f : 1f;
        var diffuse = Math.Max(0, Vector3.Dot(n, Light)) * shadow;
        var spec = MathF.Pow(Math.Max(0, Vector3.Dot(Vector3.Reflect(-Light, n), -dir)), 32) * shadow;
        var local = s.Color * (0.15f + 0.85f * diffuse) + new Vector3(spec);
        if (depth >= 3 || s.Mirror <= 0) return local;
        return Vector3.Lerp(local, Trace(p + n * 1e-3f, Vector3.Reflect(dir, n), depth + 1), s.Mirror);
    }

    private static (int Index, float T) Hit(Vector3 o, Vector3 d)
    {
        var best = float.MaxValue; var index = -1;
        for (var i = 0; i < Spheres.Length; i++)
        {
            var oc = o - Spheres[i].C; var b = Vector3.Dot(oc, d); var c = Vector3.Dot(oc, oc) - Spheres[i].R * Spheres[i].R;
            var h = b * b - c; if (h < 0) continue;
            var t = -b - MathF.Sqrt(h);
            if (t > 1e-4f && t < best) { best = t; index = i; }
        }
        return (index, best);
    }
}

// GPU test: renders a ray-marched scene off screen at 1920x1080 with Direct3D 11 on the high-performance
// adapter. Nothing is shown and no setting changes; result = frames per second x 10.
public sealed class D3D11GpuBenchmark : IGpuBenchmark
{
    private const int Width = 1920, Height = 1080;
    private const string Shader = """
        cbuffer Frame : register(b0) { float time; float3 pad; };
        float4 VS(uint id : SV_VertexID) : SV_Position { float2 uv = float2((id << 1) & 2, id & 2); return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); }
        float map(float3 p) {
            float d = p.y + 1.0;
            [unroll] for (int i = 0; i < 6; i++) {
                float3 c = float3(sin(i * 1.7 + time) * 2.5, sin(i * 2.3) * 0.5, 6.0 + cos(i * 1.3 + time) * 2.0);
                d = min(d, length(p - c) - (0.6 + 0.1 * i));
            }
            return d;
        }
        float3 normal(float3 p) { float2 e = float2(0.001, 0); return normalize(float3(map(p + e.xyy) - map(p - e.xyy), map(p + e.yxy) - map(p - e.yxy), map(p + e.yyx) - map(p - e.yyx))); }
        float4 PS(float4 pos : SV_Position) : SV_Target {
            float2 uv = (pos.xy / float2(1920, 1080)) * 2 - 1; uv.y = -uv.y; uv.x *= 1.78;
            float3 ro = float3(0, 0.5, -1), rd = normalize(float3(uv, 1.5)), col = float3(0.2, 0.12, 0.18);
            float t = 0; float3 att = 1;
            for (int b = 0; b < 3; b++) {
                bool hit = false;
                for (int s = 0; s < 96; s++) { float d = map(ro + rd * t); if (d < 0.001) { hit = true; break; } t += d; if (t > 40) break; }
                if (!hit) { col += att * lerp(float3(0.2, 0.12, 0.18), float3(0.55, 0.4, 0.5), saturate(rd.y)); break; }
                float3 p = ro + rd * t, n = normal(p), l = normalize(float3(-0.6, 1, -0.4));
                col += att * float3(0.95, 0.5, 0.7) * (0.15 + 0.85 * saturate(dot(n, l)));
                att *= 0.4; ro = p + n * 0.01; rd = reflect(rd, n); t = 0;
            }
            return float4(col, 1);
        }
        """;

    public string? Adapter()
    {
        try { using var a = PickAdapter(); return a?.Description1.Description; } catch { return null; }
    }

    private static IDXGIAdapter1? PickAdapter()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
        for (uint i = 0; factory.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).Success; i++)
        {
            if ((adapter!.Description1.Flags & AdapterFlags.Software) == 0) return adapter;
            adapter.Dispose();
        }
        return null;
    }

    public double? Run(TimeSpan duration, CancellationToken ct)
    {
        using var adapter = PickAdapter();
        if (adapter is null) return null;
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
        using var dev = device!; using var ctx = context!;
        using var target = dev.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget));
        using var rtv = dev.CreateRenderTargetView(target);
        using var vs = dev.CreateVertexShader(Compiler.Compile(Shader, "VS", "lotus-score.hlsl", "vs_5_0").Span);
        using var ps = dev.CreatePixelShader(Compiler.Compile(Shader, "PS", "lotus-score.hlsl", "ps_5_0").Span);
        using var cb = dev.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer));
        using var fence = dev.CreateQuery(new QueryDescription(QueryType.Event));
        ctx.OMSetRenderTargets(rtv); ctx.RSSetViewport(0, 0, Width, Height);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetShader(vs); ctx.PSSetShader(ps); ctx.PSSetConstantBuffer(0, cb);
        // Batches grow until each lasts >= 20 ms, so the sync gap stays small even on very fast GPUs.
        var frames = 0; var batch = 4; var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            var started = sw.Elapsed;
            for (var i = 0; i < batch; i++, frames++)
            {
                ctx.UpdateSubresource(new Vector4(frames * 0.016f, 0, 0, 0), cb);
                ctx.Draw(3, 0);
            }
            ctx.End(fence); ctx.Flush();
            var spin = new SpinWait();
            while (!ctx.IsDataAvailable(fence)) spin.SpinOnce(); // the GPU queue never runs ahead of the clock
            if ((sw.Elapsed - started).TotalMilliseconds < 20 && batch < 4096) batch *= 2;
        }
        return frames / sw.Elapsed.TotalSeconds * 10;
    }
}

public sealed class JsonScoreHistory(string path) : IScoreHistory
{
    public IReadOnlyList<ScoreRun> Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<ScoreRun>>(File.ReadAllText(path)) ?? [] : []; }
        catch (JsonException) { return []; }
    }
    public void Save(IReadOnlyList<ScoreRun> runs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(runs)); File.Move(temp, path, true);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsScoreMachine(IGpuBenchmark gpu) : IScoreMachine
{
    private string? cpu, gpuName;
    public string Cpu() => cpu ??= (Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string)?.Trim() ?? "CPU";
    public string Gpu() => gpuName ??= gpu.Adapter()?.Trim() ?? "—";
}
