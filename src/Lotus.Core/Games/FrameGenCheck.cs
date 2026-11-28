namespace Lotus.Core.Games;

// Is the game really generating frames? Gothic 1 Remake, 28/11/2026: FrameGenerationDLSS=1 in GameUserSettings.ini does not
// turn it on (the menu writes 17 for 2X), and PresentMon marked the generated frames as "Application" too; the proof was the
// count (74/s at 75 Hz against 71 without). Lotus compares the frames shown with the real (rendered) frames from the Reflex
// GPU frame time: about twice as many shown = generator on; about the same = off. PresentMon's FrameType counts when it names
// a generator. Unknown when the game gives no Reflex timings and no frame type.
public static class FrameGenCheck
{
    public const double OnRatio = 1.6, OffRatio = 1.25;

    public static bool? Seen(double shownFps, double? realFps, string? frameTypes = null)
    {
        if (frameTypes is { Length: > 0 } types && types.Split(", ").Any(t => t is not ("Application" or "Unavailable" or "NotSet" or "Unspecified" or "Repeated" or "")
                && !t.StartsWith("rtss", StringComparison.OrdinalIgnoreCase)))
            return true; // a generator named in PresentMon's FrameType
        if (realFps is not > 0 || shownFps <= 0) return null;
        var ratio = shownFps / realFps.Value;
        return ratio >= OnRatio ? true : ratio <= OffRatio ? false : null;
    }

    // The player's choice ("Uso o gerador de quadros") against what was measured; null = they agree or nothing is known.
    public static string? Mismatch(bool chosen, bool? seen) => seen switch
    {
        false when chosen => "FgChosenButOff",
        true when !chosen => "FgOnButNotChosen",
        _ => null,
    };
}
