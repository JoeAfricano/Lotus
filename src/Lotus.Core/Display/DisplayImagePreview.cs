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
        var brightness = profile.Brightness is double br && baseline.Brightness is { } levelBr ? Math.Pow(2, (br - levelBr.Percent) / 100) : 1;
        var contrast = profile.DigitalContrast / alreadyApplied.DigitalContrast * (profile.Contrast is double co && baseline.Contrast is { } levelCo ? Math.Pow(2, (co - levelCo.Percent) / 100) : 1);
        double[] gains = [(1 - profile.Warmth * .003) / (1 - alreadyApplied.Warmth * .003),
            (1 + profile.Tint * .004) / (1 + alreadyApplied.Tint * .004), (1 + profile.Warmth * .003) / (1 + alreadyApplied.Warmth * .003)]; // BGRA
        var lut = new double[3, 256];
        for (var channel = 0; channel < 3; channel++)
        for (var i = 0; i < 256; i++)
        {
            var v = Math.Clamp(((Math.Pow(i / 255d, 1 / gamma) - .5) * contrast + .5 + digitalBrightness) * gains[channel], 0, 1);
            lut[channel, i] = (v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)) * brightness;
        }
        var saturation = profile.Saturation is double sat && baseline.Vibrance is { } range
            ? Math.Clamp(sat / Math.Max(1, range.Percent(range.Current)), 0, 3) : 1;
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
