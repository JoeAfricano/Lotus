namespace Lotus.Core.Benchmark;

// How the game's frames reached the screen, from PresentMon's PresentMode column. "Direct": the game owns the screen
// or a hardware plane (Independent Flip, legacy flip): no extra frame. "Composed": Windows (DWM) copies the game into the
// desktop image first, about one refresh later.
public enum PresentPath { Unknown, Direct, Composed }

// Whether the display can keep a game on its own hardware plane while another window (an overlay window) sits above it.
// Idea from CapFrameX's MpoSupport (MIT, © DevTechProfile, github.com/CXWorld/CapFrameX); Lotus only reports it.
public enum MpoStatus { Unknown, Supported, NoOverlayPlane, SwitchedOff }

public static class PresentModes
{
    public static PresentPath Classify(string? mode) => mode switch
    {
        null or "" => PresentPath.Unknown,
        _ when mode.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase) => PresentPath.Direct,
        _ when mode.StartsWith("Composed", StringComparison.OrdinalIgnoreCase) => PresentPath.Composed,
        _ => PresentPath.Unknown,
    };

    // MPO from the driver caps and the DWM switch (OverlayTestMode = 5 turns MPO off whatever the driver says).
    // A plane for the game needs at least one RGB plane besides the base desktop plane.
    public static MpoStatus Mpo(int? overlayTestMode, int? maxPlanes, int? maxRgbPlanes)
    {
        if (overlayTestMode == 5) return MpoStatus.SwitchedOff;
        if (maxPlanes is not { } total || maxRgbPlanes is not { } rgb) return MpoStatus.Unknown;
        return Math.Min(total, rgb) - 1 >= 1 ? MpoStatus.Supported : MpoStatus.NoOverlayPlane;
    }
}
