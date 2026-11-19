namespace Lotus.Core.Display;

// A fast, explicitly illustrative SDR image preview. It does not measure backlight luminance,
// reproduce a proprietary driver transform, or provide an ICC proof. No hardware calls here.
public static class DisplayImagePreview
{
    private static readonly byte[] Encode = Enumerable.Range(0, 8193).Select(i =>
    {
        var v = i / 8192d;
        return (byte)Math.Clamp(Math.Round((v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055) * 255), 0, 255);
    }).ToArray();
    public static byte[] Render(byte[] bgra, CalibrationProfile profile, CalibrationState baseline, CalibrationProfile? alreadyApplied = null)
    {
        var output = new byte[bgra.Length]; RenderInto(bgra, output, profile, baseline, alreadyApplied); return output;
    }
    public static void RenderInto(ReadOnlySpan<byte> bgra, Span<byte> output, CalibrationProfile profile, CalibrationState baseline, CalibrationProfile? alreadyApplied = null)
    {
        if (bgra.Length % 4 != 0 || output.Length != bgra.Length || bgra.Length > 1920 * 1080 * 4 || !profile.Valid || !baseline.Valid || alreadyApplied is { Valid: false })
            throw new ArgumentException("Invalid SDR preview input.");
        alreadyApplied ??= new();
        var gamma = profile.Gamma / alreadyApplied.Gamma;
        var digitalBrightness = profile.DigitalBrightness - alreadyApplied.DigitalBrightness;
        // Backlight output is close to linear in the control on typical LCDs; the small offset keeps 0% from turning black.
        var brightness = profile.Brightness is double br && baseline.Brightness is { } levelBr ? (br + 10) / (levelBr.Percent + 10) : 1;
        var contrast = profile.DigitalContrast / alreadyApplied.DigitalContrast * (profile.Contrast is double co && baseline.Contrast is { } levelCo ? Math.Pow(2, (co - levelCo.Percent) / 100) : 1);
        var want = CalibrationProfile.WhiteGains(profile.Warmth, profile.Tint); var have = CalibrationProfile.WhiteGains(alreadyApplied.Warmth, alreadyApplied.Tint);
        double[] gains = [want[2] / have[2], want[1] / have[1], want[0] / have[0]]; // BGRA
        var lut = new double[3, 256];
        for (var channel = 0; channel < 3; channel++)
        for (var i = 0; i < 256; i++)
        {
            var v = Math.Clamp(((Math.Pow(i / 255d, 1 / gamma) - .5) * contrast + .5 + digitalBrightness) * gains[channel], 0, 1);
            lut[channel, i] = (v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)) * brightness;
        }
        // Driver intensity: the default is neutral, the maximum roughly doubles chroma and the minimum is near gray.
        var saturation = profile.Saturation is double sat && baseline.Vibrance is { } range
            ? Math.Clamp(Chroma(sat, range) / Math.Max(.05, Chroma(range.Percent(range.Current), range)), 0, 3) : 1;
        static double Chroma(double percent, ColorRange range)
        {
            double neutral = range.Percent(range.Default);
            return percent >= neutral ? 1 + (percent - neutral) / Math.Max(1, 100 - neutral) : percent / Math.Max(1, neutral);
        }
        static byte ToByte(double linear) => Encode[(int)Math.Clamp(Math.Round(linear * 8192), 0, 8192)];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var b = lut[0, bgra[i]]; var g = lut[1, bgra[i + 1]]; var r = lut[2, bgra[i + 2]];
            var y = .2126 * r + .7152 * g + .0722 * b;
            output[i] = ToByte(y + (b - y) * saturation);
            output[i + 1] = ToByte(y + (g - y) * saturation);
            output[i + 2] = ToByte(y + (r - y) * saturation);
            output[i + 3] = bgra[i + 3];
        }
    }
}
