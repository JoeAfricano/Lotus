namespace Lotus.Core.Display;

public enum DisplayUse { Everyday, Gaming, Editing, Movies }
public enum DisplayAmbient { Normal, Dim, Bright }

// One starting point per use and room. Brightness is a band in monitor control units (not nits), gamma is
// relative to the monitor's own curve (1 = untouched; above 1 opens midtones), warmth is the ramp's white tint and
// saturation boost is a share of the driver's default-to-maximum range. Values follow common references:
// sRGB/office gamma 2.2 and D65 white; photo/print work at a low 80–120 nit level; BT.1886 gamma 2.4 for films in
// a dark room and 2.2 in a lit room; competitive games lift shadows a little rather than raising black level.
public sealed record DisplayPlan(double BrightnessLow, double BrightnessHigh, double Gamma, double Warmth, double SaturationBoost);

public static class DisplayRecommendations
{
    public static DisplayPlan Plan(DisplayUse use, DisplayAmbient ambient) => (use, ambient) switch
    {
        (DisplayUse.Everyday, DisplayAmbient.Normal) => new(45, 70, 1, 0, 0),
        (DisplayUse.Everyday, DisplayAmbient.Dim) => new(30, 45, 1, 8, 0),       // comfort at night, not accuracy
        (DisplayUse.Everyday, DisplayAmbient.Bright) => new(75, 100, 1.04, 0, 0),
        (DisplayUse.Gaming, DisplayAmbient.Normal) => new(50, 75, 1.06, 0, .15),
        (DisplayUse.Gaming, DisplayAmbient.Dim) => new(35, 50, 1.04, 0, .1),
        (DisplayUse.Gaming, DisplayAmbient.Bright) => new(80, 100, 1.1, 0, .2),
        (DisplayUse.Editing, DisplayAmbient.Normal) => new(35, 50, 1, 0, 0),
        (DisplayUse.Editing, DisplayAmbient.Dim) => new(30, 40, 1, 0, 0),
        (DisplayUse.Editing, DisplayAmbient.Bright) => new(50, 65, 1, 0, 0),
        (DisplayUse.Movies, DisplayAmbient.Normal) => new(40, 60, .96, 0, 0),
        (DisplayUse.Movies, DisplayAmbient.Dim) => new(25, 40, .92, 0, 0),       // ≈ 2.2 → 2.4 (BT.1886 dark room)
        (DisplayUse.Movies, DisplayAmbient.Bright) => new(70, 100, 1, 0, 0),
        _ => throw new ArgumentException("Unsupported recommendation context.")
    };
    public static CalibrationProfile Suggest(CalibrationState original, DisplayUse use, DisplayAmbient ambient)
    {
        if (!original.CanAdjust || !Enum.IsDefined(use) || !Enum.IsDefined(ambient))
            throw new ArgumentException("Unsupported recommendation context.");
        var plan = Plan(use, ambient);
        // Keep the player's brightness when it already sits in the band; otherwise move only to the nearest edge.
        double? brightness = original.Brightness is { } level ? Math.Round(Math.Clamp(level.Percent, plan.BrightnessLow, plan.BrightnessHigh)) : null;
        var ramp = original.Ramp is not null;
        // Contrast stays at the monitor's own setting: raising it on most panels clips highlights.
        double? saturation = original.Vibrance is { } range
            ? range.Percent(range.Default + (int)Math.Round((range.Maximum - range.Default) * plan.SaturationBoost)) : null;
        return new(brightness, original.Contrast?.Percent, ramp ? plan.Gamma : 1, Warmth: ramp ? plan.Warmth : 0, Saturation: saturation);
    }
}
