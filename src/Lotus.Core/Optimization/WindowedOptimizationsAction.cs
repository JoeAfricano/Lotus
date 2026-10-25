namespace Lotus.Core.Optimization;

// Per-user DirectX setting behind Settings › Display › Graphics › "Optimizations for windowed games".
// Stored as "key=value;" pairs (DirectXUserGlobalSettings); other pairs (AutoHDREnable, VRROptimizeEnable...) are preserved.
public interface IDirectXUserSettings
{
    bool Supported { get; }
    string? Read();
    void Write(string? value); // null deletes the value
}

public sealed class WindowedOptimizationsAction(IDirectXUserSettings settings) : IOptimizationAction
{
    public const string ActionId = "windows.directx.windowed-optimizations.v1";
    public const string FeatureId = "windowed-optimizations";
    private const string Absent = "<absent>", Key = "SwapEffectUpgradeEnable";
    public ActionDefinition Definition => new(ActionId, "FeatAdvicewindowed-optimizations", ActionRisk.Low, "Windows 11 DirectX user settings (HKCU)", true);
    public Eligibility Eligibility => settings.Supported ? Eligibility.Eligible : Eligibility.Unsupported;
    public bool CanAttributeRecovery => false; // The registry has no writer token; an interrupted write stays uncertain.
    public ConfigurationState Detect() => new(settings.Read() ?? Absent, 0, null);
    // Already enabled means no change, so a differently formatted value is never rewritten.
    public ConfigurationState Plan(ConfigurationState before, Guid operation) => IsEnabled(Value(before)) ? before : new(WithEnabled(Value(before)), 0, null);
    public ConfigurationState Restore(ConfigurationState before, ConfigurationState applied, Guid operation) => before;
    public bool Exchange(ConfigurationState expected, ConfigurationState desired)
    {
        if (Detect() != expected) return false;
        settings.Write(Value(desired)); // Single per-user value, no elevation. Engine verifies afterward.
        return true;
    }

    public static bool IsEnabled(string? raw) => Pairs(raw).Any(p => p.Key == Key && p.Value == "1");
    // Sets SwapEffectUpgradeEnable=1 keeping every other pair and its order; idempotent when already enabled.
    public static string WithEnabled(string? raw)
    {
        var pairs = Pairs(raw).ToList();
        var index = pairs.FindIndex(p => p.Key == Key);
        if (index >= 0) pairs[index] = new(Key, "1"); else pairs.Add(new(Key, "1"));
        return string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
    }
    private static string? Value(ConfigurationState state) => state.Value == Absent ? null : state.Value;
    private static IEnumerable<KeyValuePair<string, string>> Pairs(string? raw) =>
        (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .Select(p => new KeyValuePair<string, string>(p[0].Trim(), p[1].Trim())).Where(p => p.Key.Length > 0);
}
