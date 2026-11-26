namespace Lotus.Core.Tools;

// NVIDIA driver "Shader Cache Size" (DRS setting 0x00AC8497 of the global profile), read only.
// Value: 0xFFFFFFFF = Unlimited, 0 = off, otherwise megabytes. DriverDefault = nobody changed it (the driver's own size).
// Stellar Blade study (26/11/2026): it was Unlimited on José's PC; each driver update empties it, so the first passes through
// each area of a game that compiles shaders while playing stutter again.
public sealed record ShaderCacheSize(uint Value, bool DriverDefault);

public static class ShaderCacheRules
{
    public const uint Unlimited = 0xFFFFFFFF;
    public const uint SmallMb = 4096; // 4 GB or less is small for current games (recent drivers default to about 10 GB)
    public static bool Small(ShaderCacheSize s) => !s.DriverDefault && s.Value != Unlimited && s.Value <= SmallMb;

    public static string Describe(ShaderCacheSize? s, ITextCatalog text, System.Globalization.CultureInfo culture) => s switch
    {
        null => text["ScNotRead"],
        { DriverDefault: true } => text["ScDefault"],
        { Value: Unlimited } => text["ScUnlimited"],
        { Value: 0 } => text["ScOff"],
        { } v => text.Format(Small(v) ? "ScSmall" : "ScSize", v.Value >= 1024 ? (v.Value / 1024.0).ToString("0.#", culture) + " GB" : v.Value + " MB"),
    };
}
