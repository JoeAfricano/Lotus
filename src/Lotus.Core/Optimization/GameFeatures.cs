namespace Lotus.Core.Optimization;

// Windows gaming features the Lotus can read reliably. Most are manual guidance (state + Settings link);
// only features with a verifiable, reversible per-user value get a real action (see WindowedOptimizationsAction).
public enum FeatureState { On, Off, SystemDefault, Unavailable }
public sealed record GameFeature(string Id, FeatureState State, string SettingsUri);
public interface IGameFeatureInventory { IReadOnlyList<GameFeature> Read(); }
public sealed record GameFeatureRow(string Id, string Name, string State, string Advice, bool NeedsAttention, string SettingsUri, bool CanApply = false, bool CanUndo = false);

public static class GameFeatureRules
{
    // Desired state per profile; null = no recommendation (depends on hardware or personal use).
    public static FeatureState? Desired(string featureId, string profileId) => (featureId, profileId) switch
    {
        ("gamemode", "game") => FeatureState.On,
        ("background-recording", "game") => FeatureState.Off,
        ("windowed-optimizations", "game") => FeatureState.On,
        ("gamemode", "daily") => FeatureState.On,
        ("display-refresh", "game" or "daily") => FeatureState.On,
        _ => null
    };
    // SystemDefault counts as aligned only where the documented Windows default matches the desired state.
    public static bool NeedsAttention(GameFeature feature, string profileId)
    {
        var desired = Desired(feature.Id, profileId);
        if (desired is null || feature.State == FeatureState.Unavailable) return false;
        var effective = feature.State == FeatureState.SystemDefault ? DefaultOf(feature.Id) : feature.State;
        return effective is not null && effective != desired;
    }
    // Game Mode is on by default; background recording is off by default. Others vary by Windows build/driver.
    public static FeatureState? DefaultOf(string featureId) => featureId switch
    {
        "gamemode" => FeatureState.On,
        "background-recording" => FeatureState.Off,
        _ => null
    };
}
