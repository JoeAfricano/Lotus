namespace Lotus.Core.Games;

// What the monitor can do for smoothness: its name, refresh rate and variable refresh rate (FreeSync / G-Sync).
// VrrPossible: the monitor supports VRR; VrrEnabled: VRR is on right now (null when the source cannot tell).
// Source: "nvapi" (NVIDIA driver, exact) | "edid" (the monitor's own identification, AMD/Intel) | "none".
public sealed record DisplaySync(string? MonitorName, int RefreshHz, bool? VrrPossible, bool? VrrEnabled, string Source)
{
    public static readonly DisplaySync Unknown = new(null, 0, null, null, "none");
    // VRR counts only when it is actually on (or, without a driver answer, when the monitor clearly supports it).
    public bool UsesVrr => VrrEnabled ?? VrrPossible ?? false;
    public bool VrrAvailableButOff => VrrPossible == true && VrrEnabled == false;

    // Reads the parts of an EDID that matter here (pure, testable): monitor name, vertical range and AMD FreeSync block.
    public static (string? Name, int MinHz, int MaxHz, bool FreeSync, bool Continuous) ParseEdid(byte[] edid)
    {
        string? name = null; int min = 0, max = 0; bool freeSync = false, continuous = false;
        if (edid.Length < 128) return (null, 0, 0, false, false);
        continuous = (edid[24] & 0x01) != 0; // EDID 1.4 feature support: continuous frequency
        for (var d = 54; d <= 108; d += 18)
        {
            if (edid[d] != 0 || edid[d + 1] != 0) continue; // detailed timing, not a descriptor
            switch (edid[d + 3])
            {
                case 0xFC: name = System.Text.Encoding.ASCII.GetString(edid, d + 5, 13).Split('\n')[0].Trim(); break;
                case 0xFD: min = edid[d + 5]; max = edid[d + 6]; break;
            }
        }
        // CTA-861 extensions: AMD's vendor block (OUI 00-00-1A) means FreeSync. DisplayID extensions (tag 0x70):
        // the Adaptive-Sync data block (tag 0x2B) means VESA Adaptive-Sync (FreeSync / G-Sync Compatible over DisplayPort).
        for (var ext = 128; ext + 128 <= edid.Length; ext += 128)
        {
            if (edid[ext] == 0x70)
            {
                var end = Math.Min(ext + 5 + edid[ext + 2], ext + 127);
                for (var b = ext + 5; b + 2 < end;) { if (edid[b] == 0x2B) freeSync = true; b += 3 + edid[b + 2]; }
                continue;
            }
            if (edid[ext] != 0x02) continue;
            var dtd = edid[ext + 2]; var i = ext + 4;
            while (i < ext + Math.Min((int)dtd, 127))
            {
                int tag = edid[i] >> 5, len = edid[i] & 0x1F;
                if (tag == 3 && len >= 3 && edid[i + 1] == 0x1A && edid[i + 2] == 0x00 && edid[i + 3] == 0x00) freeSync = true;
                i += len + 1;
            }
        }
        return (name, min, max, freeSync, continuous);
    }
}
public interface IDisplaySyncProbe { DisplaySync Read(); }
