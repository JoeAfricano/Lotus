namespace Lotus.Core.Games;

// One "Ligar" button: Lotus picks the RivaTuner mode for this game and this monitor. Returns the mode ("fluid",
// "responsive", "scanline" or null when RivaTuner's limiter should stay off) and the text key that says why.
//  - Frame generation (when the player keeps it on, or on VRR monitors of 120 Hz+, see FrameGenFits): only Reflex gets along with it (NVIDIA, Direct3D 11/12); elsewhere the game's own limit.
//  - FreeSync/G-Sync: Reflex below the refresh rate on NVIDIA (VRR keeps it smooth); otherwise front edge sync.
//  - Fixed refresh: scanline sync (no V-Sync queue) only when a measurement showed headroom: the game held the refresh
//    rate with the GPU at most 75% busy, or ran 20% above it; otherwise front edge sync with V-Sync (console-like).
public static class LimiterChoice
{
    public const double MaxGpuBusyForScanline = 75, HeadroomWithoutGpuData = 1.2;
    public const int MinRefreshForFrameGen = 120; // half of a VRR cap near 120 Hz keeps ~60 real frames

    // Frame generation makes sense only when its real half still answers fast: VRR and a monitor of 120 Hz or more.
    // Elsewhere Lotus advises turning it off and picks the limiter as for any game.
    public static bool FrameGenFits(bool adaptiveSync, int refreshHz) => adaptiveSync && refreshHz >= MinRefreshForFrameGen;

    public static (string? Mode, string Why) Pick(GamesViewModel.LowLatencyInfo info, bool adaptiveSync, int refreshHz, double? lastAverageFps, double? lastGpuBusy)
    {
        var reflex = info.Nvidia && info.ReflexFits;
        if (info.FrameGen && (info.UsesFrameGen || FrameGenFits(adaptiveSync, refreshHz)))
            return reflex ? ("responsive", info.UsesFrameGen && !FrameGenFits(adaptiveSync, refreshHz) ? "LlWhyFrameGenChosen" : "LlWhyFrameGen") : (null, "LlFrameGenOther");
        if (adaptiveSync) return reflex ? ("responsive", "LlWhyVrrReflex") : ("fluid", "LlWhyVrrFluid");
        var headroom = refreshHz > 0 && lastAverageFps is { } fps && (lastGpuBusy is { } busy
            ? busy <= MaxGpuBusyForScanline && fps >= refreshHz * 0.95
            : fps >= refreshHz * HeadroomWithoutGpuData);
        return headroom ? ("scanline", "LlWhyScanline") : ("fluid", lastAverageFps is null ? "LlWhyFluidMeasure" : "LlWhyFluid");
    }
}
