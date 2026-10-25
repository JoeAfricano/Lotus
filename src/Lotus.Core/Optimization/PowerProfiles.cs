namespace Lotus.Core.Optimization;

public sealed record PowerScheme(Guid Id, string Name);
public interface IPowerSchemeService
{
    IReadOnlyList<PowerScheme> List();
    Guid Active();
    bool? OnAcPower();
    void SetActive(Guid id);
}
public sealed record OptimizationProfile(string Id, Guid? PowerSchemeId, bool PowerEnabled = true);
public interface IProfileStore
{
    IReadOnlyList<OptimizationProfile> LoadProfiles();
    void SaveProfile(OptimizationProfile profile);
}
// Enabled follows Task Manager's StartupApproved convention; null means the approval value could not be read.
public sealed record StartupEntry(string Name, string Location, string Command, bool? Enabled = null);
public static class StartupApproval
{
    // Missing value = enabled; first byte odd (03/07) = disabled, even (02/06) = enabled.
    public static bool IsEnabled(object? value) => value is not byte[] { Length: > 0 } bytes || (bytes[0] & 1) == 0;
}
public sealed record StartupRow(string Name, string Location, string State, bool IsDisabled);
public sealed record StartupInventory(IReadOnlyList<StartupEntry> Entries, IReadOnlyList<string> Errors);
public interface IStartupInventory { StartupInventory Read(); }

public static class ProfileSuggestions
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static Guid? Suggest(string profile, IReadOnlyList<PowerScheme> schemes, bool? acPower)
    {
        if (profile == "custom") return null;
        if (profile == "game" && acPower == true && schemes.Any(s => s.Id == HighPerformance)) return HighPerformance;
        return schemes.Any(s => s.Id == Balanced) ? Balanced : null;
    }
}

public sealed class PowerSchemeAction(IPowerSchemeService power) : IOptimizationAction
{
    public const string ActionId = "windows.power.active-scheme.v1";
    public ActionDefinition Definition => new(ActionId, "PowerDescription", ActionRisk.Moderate, "Windows existing power schemes", true);
    public Eligibility Eligibility => power.List().Count > 0 ? Eligibility.Eligible : Eligibility.Unsupported;
    public bool CanAttributeRecovery => false; // OS has no revision/ownership token; interrupted writes remain uncertain.
    public ConfigurationState Detect() => new(power.Active().ToString("D"), 0, null);
    public ConfigurationState Plan(ConfigurationState before, Guid operation) => throw new InvalidOperationException("Select a power scheme.");
    public ConfigurationState Plan(ConfigurationState before, Guid operation, string? desired)
    {
        if (!Guid.TryParse(desired, out var id) || !power.List().Any(p => p.Id == id))
            throw new InvalidOperationException("Power scheme is no longer available.");
        return new(id.ToString("D"), 0, null);
    }
    public ConfigurationState Restore(ConfigurationState before, ConfigurationState applied, Guid operation) => before;
    public bool Exchange(ConfigurationState expected, ConfigurationState desired)
    {
        if (!Guid.TryParse(desired.Value, out var id) || !power.List().Any(p => p.Id == id))
            throw new InvalidOperationException("Power scheme is no longer available.");
        if (Detect() != expected) return false;
        power.SetActive(id); // Fixed API, no elevation, no parameter changes. Engine verifies afterward.
        return true;
    }
}
