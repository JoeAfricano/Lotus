namespace Lotus.Core.Telemetry;

// Voltage/frequency curve math for MSI Afterburner's control memory. Afterburner reports each point's factory frequency
// (dwFrequency, without the offset; it moves a little with temperature) and its offset separately, so a new curve is
// always computed from the factory frequencies alone (never from the offsets applied before), and checked before use.
public static class GpuCurve
{
    // Offsets (kHz) for a setting. Flat: the point at FlatMv reaches FlatMhz, points below keep the same shift, points
    // above are held at FlatMhz. Plain: the whole curve shifted by CoreOffsetMhz. Null when the voltage is not on the curve.
    public static int[]? Offsets(IReadOnlyList<int> voltageUv, IReadOnlyList<int> factoryKhz, GpuSetting setting)
    {
        var n = Math.Min(voltageUv.Count, factoryKhz.Count);
        var offsets = new int[n];
        if (setting.FlatMv is { } mv && setting.FlatMhz is { } mhz)
        {
            var at = -1;
            for (var i = 0; i < n; i++) if (voltageUv[i] >= mv * 1000) { at = i; break; }
            if (at < 0) return null;
            var shift = mhz * 1000 - factoryKhz[at];
            for (var i = 0; i < n; i++) offsets[i] = i <= at ? shift : Math.Min(shift, mhz * 1000 - factoryKhz[i]);
        }
        else for (var i = 0; i < n; i++) offsets[i] = setting.CoreOffsetMhz * 1000;
        return offsets;
    }

    // Safety check before a curve is sent: no point above the target clock (flat) and no offset beyond the family's
    // ceiling (MHz). A failed check means the setting is not applied.
    public static bool Safe(IReadOnlyList<int> factoryKhz, IReadOnlyList<int> offsets, GpuSetting setting, int maxOffsetMhz)
    {
        for (var i = 0; i < offsets.Count; i++)
        {
            if (offsets[i] > maxOffsetMhz * 1000) return false;
            if (setting.FlatMhz is { } mhz && factoryKhz[i] + offsets[i] > mhz * 1000 + 1000) return false;
        }
        return true;
    }
}
