namespace Lotus.Core.Optimization;

// Per-user (HKCU) DWORD values behind Windows Settings switches. No elevation; the previous value (or its absence) is restored exactly.
public interface IUserRegistry
{
    int? ReadDword(string path, string name);
    void WriteDword(string path, string name, int? value); // null deletes the value
}

public sealed class RegistryDwordAction(IUserRegistry registry, string actionId, string featureId, string path, string name, int desired, int absentMeans) : IOptimizationAction
{
    private const string Absent = "<absent>";
    public string FeatureId => featureId;
    public ActionDefinition Definition => new(actionId, "FeatAdvice" + featureId, ActionRisk.Low, @"HKCU\" + path, true);
    public Eligibility Eligibility => Eligibility.Eligible;
    public bool CanAttributeRecovery => false; // The registry has no writer token; an interrupted write stays uncertain.
    public ConfigurationState Detect() => new(registry.ReadDword(path, name)?.ToString() ?? Absent, 0, null);
    // Already effective (including the Windows default when the value is absent) means no change and no write.
    public ConfigurationState Plan(ConfigurationState before, Guid operation) => Effective(before) == desired ? before : new(desired.ToString(), 0, null);
    public ConfigurationState Restore(ConfigurationState before, ConfigurationState applied, Guid operation) => before;
    public bool Exchange(ConfigurationState expected, ConfigurationState target)
    {
        if (Detect() != expected) return false;
        registry.WriteDword(path, name, target.Value == Absent ? null : int.Parse(target.Value!, System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
    private int Effective(ConfigurationState state) => state.Value == Absent ? absentMeans : int.TryParse(state.Value, out var v) ? v : -1;

    // Settings › Gaming › Captures › "Record what happened" (keeps recording in the background while playing). Default off.
    public static RegistryDwordAction BackgroundRecording(IUserRegistry r) =>
        new(r, "windows.gamedvr.background-recording.v1", "background-recording", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", desired: 0, absentMeans: 0);
    // Settings › Gaming › Game Mode. Default on.
    public static RegistryDwordAction GameMode(IUserRegistry r) =>
        new(r, "windows.gamebar.game-mode.v1", "gamemode", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", desired: 1, absentMeans: 1);
}

// Primary display refresh rate: switches to the highest rate the driver offers at the current resolution.
public sealed record DisplayModeState(string Device, int Width, int Height, int CurrentHz, int MaxHz);
public interface IDisplayModes
{
    DisplayModeState? Primary();
    bool SetRefresh(string device, int hz); // false when the driver rejects the mode
}

public sealed class DisplayRefreshAction(IDisplayModes modes) : IOptimizationAction
{
    public const string ActionId = "windows.display.refresh-rate.v1";
    public const string FeatureId = "display-refresh";
    public ActionDefinition Definition => new(ActionId, "FeatAdvice" + FeatureId, ActionRisk.Low, "Primary display, modes reported by the driver", true);
    public Eligibility Eligibility => modes.Primary() is not null ? Eligibility.Eligible : Eligibility.Unsupported;
    public bool CanAttributeRecovery => false;
    public ConfigurationState Detect() => new(modes.Primary()?.CurrentHz.ToString() ?? "<absent>", 0, null);
    // A 1 Hz margin avoids rewriting 59/60 or 143/144 Hz variants of the same mode.
    public ConfigurationState Plan(ConfigurationState before, Guid operation)
    {
        var d = modes.Primary() ?? throw new InvalidOperationException("No primary display.");
        return d.MaxHz - d.CurrentHz <= 1 ? before : new(d.MaxHz.ToString(), 0, null);
    }
    public ConfigurationState Restore(ConfigurationState before, ConfigurationState applied, Guid operation) => before;
    public bool Exchange(ConfigurationState expected, ConfigurationState target)
    {
        var d = modes.Primary();
        if (d is null || Detect() != expected || !int.TryParse(target.Value, out var hz)) return false;
        return modes.SetRefresh(d.Device, hz);
    }
    public static string Label(string raw) => raw + " Hz";
}

// Per-user StartupApproved values (HKCU\...\Explorer\StartupApproved\{Run|StartupFolder}), the same marker Task Manager uses.
public interface IStartupApprovalStore
{
    byte[]? Read(string kind, string name);
    void Write(string kind, string name, byte[]? value); // null deletes the value (Windows then treats the item as enabled)
}

// Enables or disables one user startup item without deleting it. Undo restores the exact previous bytes (or absence).
public sealed class StartupToggleAction(IStartupApprovalStore store, string kind, string name, bool disable, Func<DateTime>? clock = null) : IOptimizationAction
{
    private const string Absent = "<absent>";
    public ActionDefinition Definition => new($"windows.startup.approved.v1:{kind}:{name}:{(disable ? "off" : "on")}", "PowerStartupLimits", ActionRisk.Low, @"HKCU\...\StartupApproved\" + kind, true);
    public Eligibility Eligibility => kind is "Run" or "StartupFolder" ? Eligibility.Eligible : Eligibility.Unsupported;
    public bool CanAttributeRecovery => false;
    public ConfigurationState Detect() => new(store.Read(kind, name) is { } b ? Convert.ToHexString(b) : Absent, 0, null);
    public ConfigurationState Plan(ConfigurationState before, Guid operation)
    {
        var enabled = StartupApproval.IsEnabled(before.Value == Absent ? null : Convert.FromHexString(before.Value!));
        if (enabled != disable) return before; // already in the requested state
        // 03 = disabled + FILETIME of the change (as Task Manager writes); 02 = enabled.
        var bytes = new byte[12];
        bytes[0] = disable ? (byte)3 : (byte)2;
        if (disable) BitConverter.GetBytes((clock ?? (() => DateTime.UtcNow))().ToFileTimeUtc()).CopyTo(bytes, 4);
        return new(Convert.ToHexString(bytes), 0, null);
    }
    public ConfigurationState Restore(ConfigurationState before, ConfigurationState applied, Guid operation) => before;
    public bool Exchange(ConfigurationState expected, ConfigurationState target)
    {
        if (Detect() != expected) return false;
        store.Write(kind, name, target.Value == Absent ? null : Convert.FromHexString(target.Value!));
        return true;
    }
}
