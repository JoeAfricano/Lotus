namespace Lotus.Core.Optimization;

// Catalog of small per-user Windows adjustments, chosen from the winutil tweak list (MIT, ChrisTitusTech/winutil) and
// rewritten for Lotus: each one is HKCU / per-user only (no admin), detected before writing, journaled and undone exactly.
// They are optional: offered one by one (Ferramentas › Recursos do Windows para jogos), never part of "Apply profile".
public static class CatalogFeatures
{
    public const string AccessibilityHotkeys = "accessibility-hotkeys";
    public const string MouseAcceleration = "mouse-acceleration";
    public const string TaskbarEndTask = "taskbar-end-task";
    public static readonly IReadOnlyList<string> All = [AccessibilityHotkeys, MouseAcceleration, TaskbarEndTask];
    public static bool IsCatalog(string featureId) => All.Contains(featureId);
}

// Flags of the three keyboard accessibility features (Sticky, Filter and Toggle Keys), read and written through
// SystemParametersInfo so the change applies at once and is saved for the next sign-in.
public sealed record AccessibilityKeyFlags(int Sticky, int Filter, int Toggle);
public interface IAccessibilityKeys
{
    AccessibilityKeyFlags? Read(); // null when Windows does not answer
    void Write(AccessibilityKeyFlags flags);
}

// Pressing Shift five times (or holding it 8 s, or Num Lock 5 s) opens a Windows prompt that pulls the player out of
// the game. This turns off only those shortcuts; the features themselves stay as they are (on stays on).
public sealed class AccessibilityHotkeysAction(IAccessibilityKeys keys) : IOptimizationAction
{
    public const string ActionId = "windows.accessibility.hotkeys.v1";
    public const int HotkeyActive = 0x4; // SKF_HOTKEYACTIVE = FKF_HOTKEYACTIVE = TKF_HOTKEYACTIVE
    public ActionDefinition Definition => new(ActionId, "FeatAdvice" + CatalogFeatures.AccessibilityHotkeys, ActionRisk.Low, "SystemParametersInfo (per user)", true);
    public Eligibility Eligibility => keys.Read() is not null ? Eligibility.Eligible : Eligibility.Unsupported;
    public bool CanAttributeRecovery => false;
    public ConfigurationState Detect() => new(keys.Read() is { } f ? Format(f) : "<absent>", 0, null);
    public ConfigurationState Plan(ConfigurationState before, Guid operation)
    {
        if (Parse(before.Value) is not { } f) throw new InvalidOperationException("Keyboard accessibility settings unavailable.");
        var target = new AccessibilityKeyFlags(f.Sticky & ~HotkeyActive, f.Filter & ~HotkeyActive, f.Toggle & ~HotkeyActive);
        return target == f ? before : new(Format(target), 0, null);
    }
    public ConfigurationState Restore(ConfigurationState before, ConfigurationState applied, Guid operation) => before;
    public bool Exchange(ConfigurationState expected, ConfigurationState target)
    {
        if (Detect() != expected || Parse(target.Value) is not { } f) return false;
        keys.Write(f);
        return true;
    }
    public static bool AnyShortcutOn(AccessibilityKeyFlags f) => ((f.Sticky | f.Filter | f.Toggle) & HotkeyActive) != 0;
    public static string Format(AccessibilityKeyFlags f) => FormattableString.Invariant($"{f.Sticky},{f.Filter},{f.Toggle}");
    public static AccessibilityKeyFlags? Parse(string? value)
    {
        var parts = value?.Split(',');
        return parts is { Length: 3 } && int.TryParse(parts[0], out var s) && int.TryParse(parts[1], out var fk) && int.TryParse(parts[2], out var t) ? new(s, fk, t) : null;
    }
}

// "Enhance pointer precision": Windows speeds the cursor up on fast movements. Off keeps the same hand movement =
// same cursor distance in the desktop and in games that read the Windows cursor (many older or strategy games).
// Games with raw input ignore it either way. Values: [threshold1, threshold2, acceleration] (SPI_GETMOUSE).
public interface IMouseSettings
{
    int[]? Read();
    void Write(int[] values);
}

public sealed class MouseAccelerationAction(IMouseSettings mouse) : IOptimizationAction
{
    public const string ActionId = "windows.mouse.acceleration.v1";
    public ActionDefinition Definition => new(ActionId, "FeatAdvice" + CatalogFeatures.MouseAcceleration, ActionRisk.Low, "SystemParametersInfo (per user)", true);
    public Eligibility Eligibility => mouse.Read() is { Length: 3 } ? Eligibility.Eligible : Eligibility.Unsupported;
    public bool CanAttributeRecovery => false;
    public ConfigurationState Detect() => new(mouse.Read() is { Length: 3 } v ? string.Join(',', v) : "<absent>", 0, null);
    // Only the acceleration switch changes; the thresholds stay as they were, so undo is exact.
    public ConfigurationState Plan(ConfigurationState before, Guid operation)
    {
        if (Parse(before.Value) is not { } v) throw new InvalidOperationException("Mouse settings unavailable.");
        return v[2] == 0 ? before : new(string.Join(',', v[0], v[1], 0), 0, null);
    }
    public ConfigurationState Restore(ConfigurationState before, ConfigurationState applied, Guid operation) => before;
    public bool Exchange(ConfigurationState expected, ConfigurationState target)
    {
        if (Detect() != expected || Parse(target.Value) is not { } v) return false;
        mouse.Write(v);
        return true;
    }
    public static int[]? Parse(string? value)
    {
        var parts = value?.Split(',');
        if (parts is not { Length: 3 }) return null;
        var v = new int[3];
        for (var i = 0; i < 3; i++) if (!int.TryParse(parts[i], out v[i])) return null;
        return v;
    }
}
