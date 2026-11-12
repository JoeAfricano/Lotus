using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;

namespace Lotus.Infrastructure.Games;

// Text in the game overlay through RivaTuner's documented OSD slots (RTSSSharedMemoryV2, the same channel MSI Afterburner
// uses): under the SDK's dwBusy lock, Lotus claims one slot named "Lotus", writes its text, bumps the OSD frame counter, and releases the slot when done.
[SupportedOSPlatform("windows")]
public sealed class RtssOsd(string mapName = RtssSharedMemory.DefaultMap)
{
    private const uint Signature = 0x52545353;
    private const string Owner = "Lotus";

    public bool Show(string text) => Write(Plain(text), release: false);

    // RivaTuner's OSD fonts draw only plain ASCII reliably: on the user's PC "ANÁLISE" showed as "AN :LISE" and "estável"
    // as "estvel". Accents are folded ("análise" -> "analise"), which stays readable in Portuguese.
    public static string Plain(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            folded.Append(c switch { '·' => '-', '–' or '—' => '-', '…' => '.', '“' or '”' => '"', '‘' or '’' => '\'', < (char)128 => c, _ => '?' });
        }
        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
    public bool Clear() => Write("", release: true);

    private bool Write(string text, bool release)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            if (view.ReadUInt32(0) != Signature) return false;
            uint version = view.ReadUInt32(4), entrySize = view.ReadUInt32(20), arrOffset = view.ReadUInt32(24), arrSize = view.ReadUInt32(28);
            if (entrySize < 512 || arrSize is 0 or > 256) return false;
            // v2.14+: dwBusy (offset 36) bit 0 is the writers' lock, as the SDK asks. Without it another client
            // (Afterburner, HWiNFO) could claim the same slot or mix its text with ours.
            var locked = version >= 0x0002000E && view.Capacity >= 40;
            if (locked && !TryLock(view)) return false;
            try { return WriteSlot(view, text, release, version, entrySize, arrOffset, arrSize); }
            finally { if (locked) Unlock(view); }
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { return false; } // RTSS not running
    }

    private static bool WriteSlot(MemoryMappedViewAccessor view, string text, bool release, uint version, uint entrySize, uint arrOffset, uint arrSize)
    {
        var extended = version >= 0x00020007 && entrySize >= 512 + 4096;
        for (var pass = 0; pass < 2; pass++)
            for (var i = 1u; i < arrSize; i++) // slot 0 belongs to RTSS itself
            {
                long at = arrOffset + i * (long)entrySize;
                var owner = ReadString(view, at + 256, 256);
                if (pass == 0 ? owner != Owner : owner.Length != 0) continue;
                if (release && pass == 1) return true; // nothing of ours to clear
                if (!release) WriteString(view, at + 256, 256, Owner);
                WriteString(view, extended ? at + 512 : at, extended ? 4096 : 256, text);
                if (extended) WriteString(view, at, 256, ""); // keep the legacy field empty
                if (release) WriteString(view, at + 256, 256, "");
                view.Write(32, view.ReadUInt32(32) + 1); // dwOSDFrame: tells RTSS the text changed
                return true;
            }
        return false;
    }

    // Atomic bit 0 of dwBusy: a few short tries; if another client holds it, this update is skipped (the next one retries).
    private static unsafe bool TryLock(MemoryMappedViewAccessor view)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if ((Interlocked.Or(ref Busy(view), 1) & 1) == 0) return true;
            Thread.Sleep(1);
        }
        return false;
    }
    private static unsafe void Unlock(MemoryMappedViewAccessor view) => Interlocked.And(ref Busy(view), ~1);
    private static unsafe ref int Busy(MemoryMappedViewAccessor view)
    {
        byte* p = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        try { return ref *(int*)(p + view.PointerOffset + 36); }
        finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); } // the view stays mapped while the accessor lives
    }

    private static string ReadString(MemoryMappedViewAccessor view, long at, int max)
    {
        var bytes = new byte[max]; view.ReadArray(at, bytes, 0, max);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.Latin1.GetString(bytes, 0, end < 0 ? max : end);
    }
    private static void WriteString(MemoryMappedViewAccessor view, long at, int max, string value)
    {
        var bytes = new byte[max];
        var data = Encoding.Latin1.GetBytes(value);
        Array.Copy(data, bytes, Math.Min(data.Length, max - 1));
        view.WriteArray(at, bytes, 0, max);
    }
}
