using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Lotus.Core.Score;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Lotus.Infrastructure.Score;

// CPU test: a deterministic floral ray tracer (petals, shadows, reflections). Each thread renders whole
// rows of the same scene; the result is rendered pixels per second / 100.
public sealed class CpuRayBenchmark : ICpuBenchmark, IFramePreview, IScoreExecutionEnvironment
{
    // Inspect the actual measured assembly, including mixed configurations and Optimize=false.
    public bool IsOptimized => Attribute.GetCustomAttribute(typeof(CpuRayBenchmark).Assembly,
        typeof(DebuggableAttribute)) is not DebuggableAttribute { IsJITOptimizerDisabled: true };
    private const int Width = 320, Height = 180;
    private int version;
    // Live picture for the page: each worker writes the pixels it renders (BGRA).
    public uint[] Pixels { get; } = new uint[Width * Height];
    int IFramePreview.Width => Width; int IFramePreview.Height => Height;
    public int Version => Volatile.Read(ref version);
    public double Run(int threads, TimeSpan duration, CancellationToken ct)
    {
        threads = Math.Max(1, threads);
        using var fullSpeed = new FullSpeed(); // no background throttling while measuring
        long pixels = 0; var nextRow = -1L; var stop = false;
        var sw = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            FullSpeed.ThisThread();
            long local = 0; var sink = 0f;
            while (!Volatile.Read(ref stop))
            {
                var index = Interlocked.Increment(ref nextRow);
                int row = (int)(index % Height), frame = (int)(sw.Elapsed.TotalSeconds * 30);
                for (var x = 0; x < Width; x++) { var c = Pixel(x, row, frame); sink += c.X; Pixels[row * Width + x] = Pack(c); }
                Interlocked.Increment(ref version);
                local += Width;
            }
            Interlocked.Add(ref pixels, local + (sink < -1 ? 1 : 0));
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal }).ToArray();
        foreach (var w in workers) w.Start();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested) Thread.Sleep(10);
        Volatile.Write(ref stop, true);
        foreach (var w in workers) w.Join();
        ct.ThrowIfCancellationRequested();
        return pixels / sw.Elapsed.TotalSeconds / 100.0;
    }

    private static uint Pack(Vector3 c)
    {
        static uint B(float v) => (uint)Math.Clamp(MathF.Sqrt(Math.Max(0, v)) * 255, 0, 255); // gamma 2
        return 0xFF000000u | (B(c.X) << 16) | (B(c.Y) << 8) | B(c.Z);
    }

    public static Vector3 Pixel(int x, int y, int frame = 0) => LotusCpuScene.Pixel(x,y,frame);

}

// GPU test: renders a ray-marched scene at 1920x1080 with Direct3D 11 on the high-performance adapter, in a visible
// window (shown at 1280x720; closing it cancels). No setting changes. Result = completed renders / measured
// wall time x 10, excluding Present. Includes CPU submission/synchronization; it is not isolated GPU time or game FPS.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class D3D11GpuBenchmark : IGpuBenchmark
{
    private const int Width = 1920, Height = 1080;
    // One floral workload shared by Score, tuning and preview.
    private const string TuningShader = """
        cbuffer Frame : register(b0) { float time; float3 pad; };
        float4 VS(uint id : SV_VertexID) : SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),0,1); }
        float octa(float3 p,float s) { p=abs(p); return (p.x+p.y+p.z-s)*0.57735; }
        float ellipsoid(float3 p,float3 r) { float k0=length(p/r),k1=length(p/(r*r)); return k0*(k0-1)/max(k1,0.001); }
        float petal(float3 p) { float3 r=float3(0.54,1.04,0.14); return max(ellipsoid(p+float3(0.22,0,0),r),ellipsoid(p-float3(0.22,0,0),r)); }
        float2 turn(float2 p,float a) { float c=cos(a),s=sin(a); return float2(c*p.x-s*p.y,s*p.x+c*p.y); }
        float layer(float3 p,float count,float radius,float level,float tilt,float phase) {
            float a=atan2(p.z,p.x)+phase,sector=6.283185/count;
            a-=floor(a/sector+0.5)*sector;
            float3 q=float3(cos(a)*length(p.xz)-radius,p.y-level,sin(a)*length(p.xz));
            q.xy=turn(q.xy,tilt); q.x+=0.13*q.y*q.y; q.z+=0.06*q.y*q.y;
            return petal(q)*0.58;
        }
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float noise(float2 p) { float2 i=floor(p),f=frac(p); f=f*f*(3-2*f); return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y); }
        float3 space(float3 rd) {
            float2 sky=float2(atan2(rd.x,rd.z),rd.y)*float2(1.1,1.8);
            float dust=noise(sky*3.5)*0.65+noise(sky*8)*0.25+noise(sky*17)*0.1;
            float cloud=exp(-24*pow(sky.y+0.16+0.13*sin(sky.x*3),2))*smoothstep(0.26,0.8,dust);
            float2 cells=sky*float2(220,140),f=frac(cells)-0.5;
            float stars=step(0.989,hash(floor(cells)))*exp(-dot(f,f)*220);
            return float3(0.002,0.003,0.008)+cloud*float3(0.065,0.018,0.087)+stars*float3(0.62,0.66,0.83);
        }
        float asteroids(float3 p) {
            float d=100;
            [unroll] for(int i=0;i<4;i++) {
                float x=frac(time*0.009+i*0.25)*32-16;
                float3 q=p-float3(x,3.0+sin(i*2.3)*0.9,13.0+i*2.1);
                q.xy=turn(q.xy,time*0.13+i); q.yz=turn(q.yz,time*0.09+i*0.8);
                q*=float3(0.78,1.18,1.04); // elongated fragments, rather than round stones
                float size=0.40+0.13*sin(i*1.7);
                float facets=max(octa(q,size*1.45),dot(q,normalize(float3(1,-0.6,0.4)))-size*0.62);
                float rock=max(length(q)-size,facets);
                rock+=0.018*sin(q.x*16+i)*sin(q.y*13)*sin(q.z*17);
                d=min(d,rock*0.7);
            }
            return d;
        }
        float sculpture(float3 p) {
            p-=float3(0,0.22,5.2); p.xz=turn(p.xz,time*0.045);
            float d=layer(p,9,0.88,-0.12,1.04,0);
            d=min(d,layer(p,7,0.43,0.22,0.45,0.32));
            d=min(d,layer(p*1.5,5,0.12,0.62,0.12,0.12)/1.5);
            float3 base=p-float3(0,-1.2,0);
            float ring=length(float2(length(base.xz)-1.86,base.y))-0.022;
            return min(d,ring);
        }
        float floorDistance(float3 p) { return p.y+1.18+0.045*sin(p.x*1.4+time*0.1)*cos(p.z*1.1); }
        float map(float3 p) { return min(asteroids(p),min(floorDistance(p)*0.9,sculpture(p))); }
        float3 normal(float3 p) { float2 e=float2(0.001,0); return normalize(float3(map(p+e.xyy)-map(p-e.xyy),map(p+e.yxy)-map(p-e.yxy),map(p+e.yyx)-map(p-e.yyx))); }
        float4 PS(float4 pos:SV_Position):SV_Target {
            float2 uv=pos.xy/float2(1920,1080)*2-1; uv.y=-uv.y; uv.x*=1.77778;
            float3 ro=float3(0,1.85,-2.6),rd=normalize(float3(uv.x,uv.y-0.43,2.25));
            float3 sun=float3(0,0.55,5.2);
            float3 nearest=ro+rd*max(0,dot(sun-ro,rd));
            float radius=length(nearest-sun),halo=exp(-radius*radius*0.68);
            float corona=exp(-abs(radius-1.35)*5)*(0.75+0.25*cos(atan2(nearest.y-sun.y,nearest.x)*18));
            float3 col=float3(0.19,0.066,0.014)*halo+float3(0.09,0.025,0.008)*corona,att=1;
            [loop] for(int b=0;b<3;b++) {
                float t=0; bool hit=false;
                [loop] for(int s=0;s<128;s++) { float d=map(ro+rd*t); if(d<0.0015){hit=true;break;} t+=max(d,0.0005); if(t>35)break; }
                if(!hit) { col+=att*space(rd); break; }
                float3 p=ro+rd*t,n=normal(p),l=normalize(float3(-0.5,0.9,-0.6));
                bool floorHit=floorDistance(p)<0.006;
                bool rockHit=asteroids(p)<0.008;
                float3 metal=lerp(float3(0.18,0.047,0.083),float3(0.82,0.50,0.33),saturate(n.y*0.6+0.35));
                if(!floorHit&&!rockHit)metal=lerp(metal,float3(1.0,0.62,0.22),0.32);
                float vein=pow(saturate(1-abs(sin(p.x*13+p.y*9+noise(p.zy*4)*3))*7),3);
                if(rockHit)metal=lerp(float3(0.055,0.045,0.09),float3(0.33,0.15,0.24),noise(p.xy*12));
                if(floorHit)metal=float3(0.002,0.004,0.012);
                float diffuse=0.14+0.68*saturate(dot(n,l));
                float spec=pow(saturate(dot(reflect(-l,n),-rd)),48)+0.8*pow(saturate(dot(reflect(normalize(float3(-0.8,-0.3,-0.2)),n),-rd)),72);
                float edge=pow(1-saturate(dot(-rd,n)),3);
                float ringGlow=exp(-24*abs(length(p.xz-float2(0,5.2))-1.86));
                col+=att*(metal*diffuse+float3(0.85,0.74,0.68)*spec*0.6+float3(0.28,0.045,0.10)*edge*0.3);
                if(!floorHit&&!rockHit)col+=att*(float3(0.12,0.043,0.01)+float3(0.20,0.07,0.018)*edge);
                if(rockHit)col+=att*(vein*float3(0.62,0.26,0.065)+edge*float3(0.13,0.06,0.20));
                if(floorHit) {
                    float2 grid=abs(frac(p.xz*0.55)-0.5);
                    float wire=1-smoothstep(0.006,0.022,min(grid.x,grid.y));
                    float fade=exp(-0.055*length(p.xz-float2(0,5.2)));
                    col+=att*(float3(0.19,0.018,0.065)*ringGlow+float3(0.012,0.028,0.065)*wire*fade);
                    col=lerp(col,col+att*space(reflect(rd,n))*0.36,0.6);
                    if(b==0)col=lerp(col,space(rd),saturate((t-16)/16));
                }
                att*=floorHit?0.32:0.24; ro=p+n*0.008; rd=reflect(rd,n);
            }
            col=col/(1+col); col=pow(max(col,0),0.4545);
            col*=1-0.22*smoothstep(0.3,2.1,length(uv));
            return float4(col,1);
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

    // Overlay preview: the same 3D scene in its own process ("Lotus.App.exe --overlay-preview"), synced to the monitor,
    // until the window is closed. RivaTuner hooks it like a game, so the user sees the overlay without opening one.
    public const string PreviewArgument = "--overlay-preview";
    public static bool Preview()
    {
        using var adapter = PickAdapter();
        if (adapter is null) return false;
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
        using var dev = device!; using var ctx = context!;
        using var vs = dev.CreateVertexShader(Compiler.Compile(TuningShader, "VS", "lotus-preview.hlsl", "vs_5_0").Span);
        using var ps = dev.CreatePixelShader(Compiler.Compile(TuningShader, "PS", "lotus-preview.hlsl", "ps_5_0").Span);
        using var cb = dev.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer));
        var hwnd = SceneWindow.Create("Lotus · prévia do overlay · F11 tela cheia · Esc voltar", 1280, 720);
        try
        {
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1(Width, Height, Format.B8G8R8A8_UNorm)
            { BufferCount = 2, SwapEffect = SwapEffect.FlipDiscard, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new(1, 0), Scaling = Scaling.Stretch, AlphaMode = AlphaMode.Ignore };
            using var swap = factory.CreateSwapChainForHwnd(dev, hwnd, desc);
            factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAll);
            using var back = swap.GetBuffer<ID3D11Texture2D>(0);
            using var rtv = dev.CreateRenderTargetView(back);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.VSSetShader(vs); ctx.PSSetShader(ps); ctx.PSSetConstantBuffer(0, cb);
            var animation = Stopwatch.StartNew();
            while (SceneWindow.Pump(hwnd))
            {
                ctx.OMSetRenderTargets(rtv); ctx.RSSetViewport(0, 0, Width, Height); // flip model unbinds after Present
                ctx.UpdateSubresource(new Vector4((float)animation.Elapsed.TotalSeconds, 0, 0, 0), cb);
                ctx.Draw(3, 0);
                swap.Present(1, PresentFlags.None); // vsync: light on the GPU, like a game menu
            }
            return true;
        }
        finally { SceneWindow.Destroy(hwnd); }
    }

    public IReadOnlyList<double>? Run(int passes, TimeSpan each, Action<int> onPass, CancellationToken ct)
    {
        using var fullSpeed = new FullSpeed(); FullSpeed.ThisThread();
        using var adapter = PickAdapter();
        if (adapter is null) return null;
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
        using var dev = device!; using var ctx = context!;
        var shader = TuningShader;
        using var vs = dev.CreateVertexShader(Compiler.Compile(shader, "VS", "lotus-score.hlsl", "vs_5_0").Span);
        using var ps = dev.CreatePixelShader(Compiler.Compile(shader, "PS", "lotus-score.hlsl", "ps_5_0").Span);
        using var cb = dev.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer));
        var hwnd = SceneWindow.Create("Lotus Score · GPU", 1280, 720);
        try
        {
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1(Width, Height, Format.B8G8R8A8_UNorm)
            {
                BufferCount = 1, SwapEffect = SwapEffect.Discard, // blt model: Present(0) is not tied to the monitor refresh in a window
                BufferUsage = Usage.RenderTargetOutput, SampleDescription = new(1, 0),
                Scaling = Scaling.Stretch, AlphaMode = AlphaMode.Ignore, Flags = SwapChainFlags.None
            };
            using var swap = factory.CreateSwapChainForHwnd(dev, hwnd, desc);
            factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAll);
            using var back = swap.GetBuffer<ID3D11Texture2D>(0);
            using var rtv = dev.CreateRenderTargetView(back);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.VSSetShader(vs); ctx.PSSetShader(ps); ctx.PSSetConstantBuffer(0, cb);
            using var fence = dev.CreateQuery(new QueryDescription(QueryType.Event));
            // Measure submitted work through completion BEFORE Present. A player's RTSS cap or compositor wait must
            // not become the measured GPU speed. Display FPS in the overlay remains real presented FPS.
            var results = new List<double>(); var batch = 1;
            var animation = Stopwatch.StartNew();
            for (var pass = 0; pass <= passes; pass++)
            {
                onPass(pass);
                var counted = 0; var sw = Stopwatch.StartNew(); var title = TimeSpan.Zero; var workSeconds = 0.0;
                while (sw.Elapsed < each)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!SceneWindow.Pump(hwnd)) throw new OperationCanceledException("Scene window closed."); // the user closed it
                    var started = sw.Elapsed;
                    ctx.OMSetRenderTargets(rtv); ctx.RSSetViewport(0, 0, Width, Height);
                    ctx.UpdateSubresource(new Vector4((float)animation.Elapsed.TotalSeconds, 0, 0, 0), cb);
                    for (var i = 0; i < batch; i++, counted++) ctx.Draw(3, 0);
                    ctx.End(fence); ctx.Flush();
                    var spin = new SpinWait();
                    while (!ctx.IsDataAvailable(fence)) { ct.ThrowIfCancellationRequested(); spin.SpinOnce(); }
                    var completed=sw.Elapsed-started; workSeconds+=completed.TotalSeconds;
                    swap.Present(0, PresentFlags.None);
                    if (completed.TotalMilliseconds < 8 && batch < 4096) batch *= 2;
                    else if (completed.TotalMilliseconds > 25 && batch > 1) batch=Math.Max(1,batch/2);
                    if (sw.Elapsed - title > TimeSpan.FromMilliseconds(500))
                    {
                        title = sw.Elapsed;
                        SceneWindow.SetTitle(hwnd, $"Lotus Score · GPU · {(pass == 0 ? "warm-up" : $"{pass}/{passes}")} · {counted / sw.Elapsed.TotalSeconds:0} FPS");
                    }
                }
                if (pass > 0) results.Add(workSeconds>0?counted/workSeconds*10:0);
            }
            return results;
        }
        finally { SceneWindow.Destroy(hwnd); }
    }
}

// Minimal Win32 window owned by the benchmark thread (messages are pumped between frames).
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class SceneWindow
{
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y, priv; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect { public int left, top, right, bottom; }
    private const string ClassName = "LotusScoreScene";
    private const uint Style = 0x00CF0000 | 0x10000000; // resizable, maximize, minimize and caption
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, Rect> Fullscreen = new();
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int size; public Rect monitor, work; public uint flags; }
    private static readonly WndProc Proc = (h, m, w, l) => DefWindowProcW(h, m, w, l); // kept alive for the process lifetime
    private static bool registered;
    private static readonly object Gate = new();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WndClassEx c);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr CreateWindowExW(int ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PeekMessageW(out Msg m, IntPtr h, uint min, uint max, uint remove);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg m);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg m);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool SetWindowTextW(IntPtr h, string text);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AdjustWindowRect(ref Rect r, uint style, bool menu);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr inst, IntPtr id);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

    public static IntPtr Create(string title, int width, int height)
    {
        var inst = GetModuleHandleW(null);
        lock (Gate)
        {
            if (!registered)
            {
                var wc = new WndClassEx
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WndClassEx>(), lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(Proc),
                    hInstance = inst, hCursor = LoadCursorW(IntPtr.Zero, 32512), lpszClassName = ClassName
                };
                if (RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("Could not register the scene window.");
                registered = true;
            }
        }
        var r = new Rect { right = width, bottom = height }; AdjustWindowRect(ref r, Style, false);
        var hwnd = CreateWindowExW(0, ClassName, title, Style, unchecked((int)0x80000000), unchecked((int)0x80000000), r.right - r.left, r.bottom - r.top, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        return hwnd != IntPtr.Zero ? hwnd : throw new InvalidOperationException("Could not create the scene window.");
    }
    // false once the user closed the window.
    public static bool Pump(IntPtr hwnd)
    {
        while (PeekMessageW(out var m, IntPtr.Zero, 0, 0, 1))
        {
            if (m.hwnd == hwnd && m.message == 0x100 && (m.lParam.ToInt64() & (1L << 30)) == 0)
            {
                if (m.wParam.ToInt64() == 0x7A) { ToggleFullscreen(hwnd); continue; }
                if (m.wParam.ToInt64() == 0x1B && Fullscreen.ContainsKey(hwnd)) { ToggleFullscreen(hwnd); continue; }
            }
            TranslateMessage(ref m); DispatchMessageW(ref m);
        }
        return IsWindow(hwnd);
    }
    private static void ToggleFullscreen(IntPtr hwnd)
    {
        if (Fullscreen.TryRemove(hwnd,out var previous))
        {
            SetWindowLongPtrW(hwnd,-16,new IntPtr(Style));
            SetWindowPos(hwnd,IntPtr.Zero,previous.left,previous.top,previous.right-previous.left,previous.bottom-previous.top,0x34);
        }
        else
        {
            var monitor=new MonitorInfo { size=System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
            if (!GetWindowRect(hwnd,out var bounds) || !GetMonitorInfoW(MonitorFromWindow(hwnd,2),ref monitor)) return;
            Fullscreen[hwnd]=bounds;
            SetWindowLongPtrW(hwnd,-16,new IntPtr(unchecked((int)0x90000000)));
            var r=monitor.monitor;
            SetWindowPos(hwnd,IntPtr.Zero,r.left,r.top,r.right-r.left,r.bottom-r.top,0x34);
        }
    }
    public static void SetTitle(IntPtr hwnd, string text) => SetWindowTextW(hwnd, text);
    public static void Destroy(IntPtr hwnd) { Fullscreen.TryRemove(hwnd,out _); if (IsWindow(hwnd)) DestroyWindow(hwnd); Pump(hwnd); }
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
