using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Games;
using Vortice.DXGI;

namespace Lotus.Infrastructure.Games;

[SupportedOSPlatform("windows")]
public sealed class WindowsGameDisplay : IGameDisplayControl
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct Device
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Name;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Hardware;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
        public short Spec,Driver,Size,Extra;
        public int Fields,X,Y,Orientation,Fixed;
        public short Color,Duplex,YResolution,Option,Collate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Form;
        public short LogPixels;
        public int Bits,Width,Height,Flags,Hz,IcmMethod,IcmIntent,Media,Dither,Reserved1,Reserved2,PanWidth,PanHeight;
    }
    private static Mode NewMode() => new() { Size=(short)Marshal.SizeOf<Mode>() };
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device,int i,ref Device data,int flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device,int i,ref Mode mode);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device,ref Mode mode,IntPtr hwnd,int flags,IntPtr param);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags,ref uint paths,byte[] pathInfo,ref uint modes,byte[] modeInfo,IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(byte[] packet);

    public IReadOnlyList<GameDisplayMode> Modes()
    {
        var result = new List<GameDisplayMode>();
        for(var i=0;i<32;i++)
        {
            var device=new Device {Size=Marshal.SizeOf<Device>()};
            if(!EnumDisplayDevices(null,i,ref device,0)) break;
            if((device.Flags&1)==0 || (device.Flags&8)!=0) continue;
            var current=NewMode(); if(!EnumDisplaySettings(device.Id,-1,ref current)) continue;
            for(var j=0;j<4096;j++)
            {
                var mode=NewMode(); if(!EnumDisplaySettings(device.Id,j,ref mode)) break;
                // Never trade resolution, color depth, orientation or progressive scanning for a higher Hz.
                if(mode.Width!=current.Width || mode.Height!=current.Height || mode.Bits!=current.Bits || (mode.Flags&2)!=0 || mode.Hz<20 || mode.Hz>2000) continue;
                result.Add(new(device.Id,device.Name,mode.Width,mode.Height,mode.Hz));
            }
        }
        var rates=ExactModes();
        return result.DistinctBy(m=>(m.Device,m.Width,m.Height,m.Hz)).Select(m=>
        {
            var exact=rates.Where(r=>r.Device==m.Device&&r.Width==m.Width&&r.Height==m.Height&&Math.Abs(r.Rate-m.Hz)<1.1)
                .OrderBy(r=>Math.Abs(r.Rate-m.Hz)).FirstOrDefault();
            return m with {ExactHz=exact?.Rate??0};
        }).OrderBy(m=>m.Device).ThenBy(m=>m.Hz).ToArray();
    }
    // Read-only rational refresh rates; EnumDisplaySettings alone rounds fractional timings.
    private static IReadOnlyList<GameDisplayMode> ExactModes()
    {
        var result=new List<GameDisplayMode>();
        try
        {
            using var factory=DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for(uint a=0;factory.EnumAdapters1(a,out var adapter).Success;a++)
            using(adapter)
            for(uint o=0;adapter.EnumOutputs(o,out var output).Success;o++)
            using(output)
            {
                var modes=output.GetDisplayModeList(Format.R8G8B8A8_UNorm,0);
                foreach(var m in modes)
                    if(m.RefreshRate.Denominator>0)
                        result.Add(new(output.Description.DeviceName,"",(int)m.Width,(int)m.Height,0,
                            m.RefreshRate.Numerator/(double)m.RefreshRate.Denominator));
            }
        }
        catch(Exception ex) when(ex is SharpGen.Runtime.SharpGenException or COMException) { }
        return result;
    }
    public GameDisplayMode? Current(string device)
    {
        var mode=NewMode();
        return EnumDisplaySettings(device,-1,ref mode) ? new(device,device,mode.Width,mode.Height,mode.Hz,ExactRate(device)) : null;
    }
    public bool SetHz(GameDisplayMode expected,int hz)
    {
        if(Current(expected.Device) is not { } before || !before.SameSetting(expected)) return false;
        if(!Modes().Any(m=>m.Device==expected.Device && m.Width==expected.Width && m.Height==expected.Height && m.Hz==hz)) return false;
        var mode=NewMode(); if(!EnumDisplaySettings(expected.Device,-1,ref mode)) return false;
        mode.Fields=0x400000; mode.Hz=hz; // DM_DISPLAYFREQUENCY only; no CDS_UPDATEREGISTRY
        if(ChangeDisplaySettingsEx(expected.Device,ref mode,IntPtr.Zero,2,IntPtr.Zero)!=0) return false;
        if(Current(expected.Device) is not { } tested || !tested.SameSetting(expected)) return false;
        return ChangeDisplaySettingsEx(expected.Device,ref mode,IntPtr.Zero,0,IntPtr.Zero)==0;
    }
    private static double ExactRate(string device)
    {
        if(GetDisplayConfigBufferSizes(2,out var count,out var modes)!=0 || count>64 || modes>256) return 0;
        var paths=new byte[count*72]; var data=new byte[modes*64];
        if(QueryDisplayConfig(2,ref count,paths,ref modes,data,IntPtr.Zero)!=0) return 0;
        for(var i=0;i<count;i++)
        {
            var offset=i*72; var packet=new byte[84];
            BitConverter.GetBytes(1).CopyTo(packet,0); BitConverter.GetBytes(84).CopyTo(packet,4);
            Array.Copy(paths,offset,packet,8,12); // source adapter LUID + source id
            if(DisplayConfigGetDeviceInfo(packet)!=0) continue;
            var name=System.Text.Encoding.Unicode.GetString(packet,20,64).TrimEnd('\0');
            if(!string.Equals(name,device,StringComparison.OrdinalIgnoreCase)) continue;
            var denominator=BitConverter.ToUInt32(paths,offset+52);
            return denominator>0 ? BitConverter.ToUInt32(paths,offset+48)/(double)denominator : 0;
        }
        return 0;
    }
}

public sealed class JsonDisplayRecovery(string? path=null) : IDisplayRecoveryStore
{
    private readonly string file=path??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lotus","display-session.json");
    public DisplayRecovery? Load() => File.Exists(file)?JsonSerializer.Deserialize<DisplayRecovery>(File.ReadAllText(file)):null;
    public void Save(DisplayRecovery? recovery)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp=file+".tmp"; File.WriteAllText(tmp,JsonSerializer.Serialize(recovery)); File.Move(tmp,file,true);
    }
}

// Foreground events, not a fast process-list polling loop. The callback never performs a display change.
[SupportedOSPlatform("windows")]
public sealed class ForegroundEvents : IDisposable
{
    private delegate void Callback(IntPtr hook,uint evt,IntPtr window,int obj,int child,uint thread,uint time);
    private readonly Callback callback;
    private readonly IntPtr hook;
    public ForegroundEvents(Action changed)
    {
        callback=(_,_,_,_,_,_,_)=>changed(); hook=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
        if(hook==IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
    }
    public static int ProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(),out var pid); return (int)pid;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,Callback callback,uint pid,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    public void Dispose() { if(hook!=IntPtr.Zero) UnhookWinEvent(hook); GC.KeepAlive(callback); }
}
