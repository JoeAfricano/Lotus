using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Telemetry;

namespace Lotus.Infrastructure.Integrations;

// Applies GPU settings through MSI Afterburner's documented control memory ("MACMSharedMemory", SDK\Include\
// MACMSharedMemory.h, v2.3): core/memory clock offsets and the voltage/frequency curve. No administrator rights, no
// Afterburner files touched; Afterburner applies the values within its own limits and reads them back. The state found
// before the first change is journaled to disk, so a crash or power cut is undone at the next start.
[SupportedOSPlatform("windows")]
public sealed class AfterburnerControl(string? journalPath = null) : IGpuControl
{
    private const string MapName = "MACMSharedMemory";
    private const uint Signature = 0x4D41434D; // 'MACM'
    private const int CmdOffset = 32, EntryStart = 36;
    private const uint CommandFlush = 0x00AB0001;
    // Entry layout (DWORD index): power 43..46, core boost 47..50, memory boost 51..54, VF curve at 69 (version, flags,
    // points, 256 × {uV, kHz, offset kHz}, lock index).
    private const int PowerCur = 43 * 4, CoreBoostCur = 47 * 4, CoreBoostMax = 49 * 4, MemBoostCur = 51 * 4, MemBoostMax = 53 * 4, Vf = 69 * 4;
    private readonly string journal = journalPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "gpu-tune-journal.json");

    public sealed record State(int PowerLimit, int CoreBoostKHz, int MemBoostKHz, int[] Offsets, int LockIndex);

    private static MemoryMappedFile? Open() { try { return MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite); } catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { return null; } }

    public bool Available
    {
        get
        {
            using var map = Open(); if (map is null) return false;
            using var v = map.CreateViewAccessor();
            return v.ReadUInt32(0) == Signature && v.ReadUInt32(12) >= 1 && v.ReadUInt32(4) >= 0x00020003;
        }
    }

    public (int CoreMaxMhz, int MemMaxMhz) Limits
    {
        get
        {
            using var map = Open(); if (map is null) return (0, 0);
            using var v = map.CreateViewAccessor();
            return (v.ReadInt32(EntryStart + CoreBoostMax) / 1000, v.ReadInt32(EntryStart + MemBoostMax) / 1000);
        }
    }

    public State? Read()
    {
        using var map = Open(); if (map is null) return null;
        using var v = map.CreateViewAccessor();
        if (v.ReadUInt32(0) != Signature) return null;
        var e = EntryStart; var points = Math.Min(256, v.ReadInt32(e + Vf + 8));
        var offsets = new int[points];
        for (var i = 0; i < points; i++) offsets[i] = v.ReadInt32(e + Vf + 12 + i * 12 + 8);
        return new(v.ReadInt32(e + PowerCur), v.ReadInt32(e + CoreBoostCur), v.ReadInt32(e + MemBoostCur), offsets, v.ReadInt32(e + Vf + 12 + 256 * 12));
    }

    public bool HasPendingOriginal => File.Exists(journal);

    public bool SaveOriginal()
    {
        if (File.Exists(journal)) return true; // an unfinished session: keep the oldest state
        if (Read() is not { } s) return false;
        try { Directory.CreateDirectory(Path.GetDirectoryName(journal)!); File.WriteAllText(journal, JsonSerializer.Serialize(s)); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Warning(ex, "GPU tune journal"); return false; }
    }

    public bool RestoreOriginal()
    {
        try
        {
            if (!File.Exists(journal)) return true;
            var s = JsonSerializer.Deserialize<State>(File.ReadAllText(journal));
            if (s is null || !Write(s)) return false;
            File.Delete(journal);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Serilog.Log.Warning(ex, "GPU tune restore"); return false; }
    }

    public bool Apply(GpuSetting setting)
    {
        using var map = Open(); if (map is null) return false;
        using var v = map.CreateViewAccessor();
        if (v.ReadUInt32(0) != Signature) return false;
        var e = EntryStart; var points = Math.Min(256, v.ReadInt32(e + Vf + 8));
        // The card's own curve without any offset: current frequency minus the offset already applied.
        var uv = new int[points]; var baseKhz = new int[points];
        for (var i = 0; i < points; i++)
        {
            var p = e + Vf + 12 + i * 12;
            uv[i] = v.ReadInt32(p); baseKhz[i] = v.ReadInt32(p + 4) - v.ReadInt32(p + 8);
        }
        var offsets = new int[points];
        if (setting.FlatMv is { } mv && setting.FlatMhz is { } mhz)
        {
            // Shift so the point at mv reaches mhz; every point above stays at mhz (flat top).
            var at = Array.FindIndex(uv, x => x >= mv * 1000);
            if (at < 0) return false;
            var shift = mhz * 1000 - baseKhz[at];
            for (var i = 0; i < points; i++) offsets[i] = i <= at ? shift : mhz * 1000 - baseKhz[i];
        }
        else for (var i = 0; i < points; i++) offsets[i] = setting.CoreOffsetMhz * 1000;
        var coreBoost = setting.FlatMv is null ? setting.CoreOffsetMhz * 1000 : 0;
        return Write(new(v.ReadInt32(e + PowerCur + 12), coreBoost, setting.MemOffsetMhz * 1000, offsets, 0)); // power limit: card default
    }

    private bool Write(State s)
    {
        using var map = Open(); if (map is null) return false;
        using var v = map.CreateViewAccessor();
        if (v.ReadUInt32(0) != Signature) return false;
        var e = EntryStart; var points = Math.Min(Math.Min(256, v.ReadInt32(e + Vf + 8)), s.Offsets.Length);
        int Clamp(int value, int curOffset) => Math.Clamp(value, v.ReadInt32(e + curOffset + 4), v.ReadInt32(e + curOffset + 8));
        v.Write(e + PowerCur, Clamp(s.PowerLimit, PowerCur));
        v.Write(e + CoreBoostCur, Clamp(s.CoreBoostKHz, CoreBoostCur));
        v.Write(e + MemBoostCur, Clamp(s.MemBoostKHz, MemBoostCur));
        for (var i = 0; i < points; i++) v.Write(e + Vf + 12 + i * 12 + 8, Math.Clamp(s.Offsets[i], v.ReadInt32(e + CoreBoostCur + 4), v.ReadInt32(e + CoreBoostCur + 8)));
        v.Write(e + Vf + 12 + 256 * 12, s.LockIndex);
        v.Write(CmdOffset, CommandFlush);
        // Afterburner clears the command after applying on its next polling pass.
        for (var i = 0; i < 40; i++) { Thread.Sleep(100); if (v.ReadUInt32(CmdOffset) == 0) return true; }
        return false;
    }
}
