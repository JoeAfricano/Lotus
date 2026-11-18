namespace Lotus.Core.Display;

public enum DisplayUse { Everyday, Gaming, Editing, Movies }
public enum DisplayAmbient { Normal, Dim, Bright }

// Preference starting points, not measured calibration targets. Percentages are monitor control units,
// never luminance in nits. Preserve contrast/white balance and any existing baseline calibration.
public static class DisplayRecommendations
{
    public static CalibrationProfile Suggest(CalibrationState original, DisplayUse use, DisplayAmbient ambient)
    {
        if (!original.CanAdjust || !Enum.IsDefined(use) || !Enum.IsDefined(ambient))
            throw new ArgumentException("Unsupported recommendation context.");
        double? brightness = original.Brightness?.Percent;
        if (brightness is double value && use != DisplayUse.Editing)
            brightness = Math.Clamp(value + (ambient switch { DisplayAmbient.Dim => -8, DisplayAmbient.Bright => 5, _ => 0 }), 0, 100);
        // An optional mild relative lift for visibility, not a claim to achieve absolute gamma 2.2.
        var gamma = use == DisplayUse.Gaming && original.Ramp is not null ? 1.04 : 1;
        // Driver default is the neutral color-intensity setting, not a measured gamut correction.
        // Gaming may add a small optional preference boost; all accuracy-oriented uses stay neutral.
        double? saturation = original.Vibrance is { } range
            ? range.Percent(use == DisplayUse.Gaming ? range.Default + (int)Math.Round((range.Maximum - range.Default) * .1) : range.Default) : null;
        return new(brightness, original.Contrast?.Percent, gamma, Saturation: saturation);
    }
}
