namespace Lotus.Core.Maintenance;

// ---- Compressão de memória + SysMain (Windows em dia) ----
// Stellar Blade study (26/11/2026, 16 GB PC): both were off (likely an "optimizer"); turning them on plus a restart took hitches
// above 50 ms from a median of 14.5 to 3.5 per 5 minutes and free RAM from ~0.4 to 1–2 GB. The two were not measured apart from the restart.
public enum SysMainStart { Unknown, Automatic, Manual, Disabled }

// Active = Windows' "Memory Compression" process is running now. Saved = what Lotus recorded before turning it on (for Desfazer).
public sealed record MemoryCompressionStatus(bool Active, SysMainStart SysMain, bool SysMainRunning, long TotalRamBytes, MemoryCompressionBefore? Saved, bool RestartPending);
public sealed record MemoryCompressionBefore(bool Compression, SysMainStart SysMain, bool SysMainRunning, DateTimeOffset ChangedAt);
// Started = the administrator prompt was accepted. Ok = Windows reports the requested configuration afterwards.
public sealed record MemoryCompressionResult(bool Started, bool Ok, string? Error);

public interface IMemoryCompression
{
    MemoryCompressionStatus Read();
    Task<MemoryCompressionResult> EnableAsync(CancellationToken token = default); // UAC; records the previous state first
    Task<MemoryCompressionResult> UndoAsync(CancellationToken token = default);   // UAC; restores the recorded state exactly
}

public static class MemoryCompressionRules
{
    public const long SmallRam = 17L << 30; // "até 16 GB" (Windows reports a bit less than the installed amount)
    public static bool IsOn(MemoryCompressionStatus s) => s.Active && s.SysMain != SysMainStart.Disabled && s.SysMainRunning;
    public static bool Recommended(MemoryCompressionStatus s) => !IsOn(s) && !s.RestartPending && s.TotalRamBytes is > 0 and <= SmallRam;
}
