using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core;

public sealed record InterfacePreferences
{
    public int SchemaVersion { get; init; } = 1;
    public string Language { get; init; } = "pt-BR";
    public string Theme { get; init; } = "Midnight";
    public bool SidebarExpanded { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public bool StartWithWindows { get; init; }   // opens minimized to the tray at sign-in
    public bool CloseToTray { get; init; } = true; // closing the window keeps Lotus in the background (hotkey keeps working)
    public bool UiSounds { get; init; } = true;    // soft interface sounds
    public int UiSoundVolume { get; init; } = 60; // 0–100    // First-run guided setup on Home (analyse, ask, apply what was accepted). Shown once; Settings can run it again.
    public bool GuidedDone { get; init; }
    public DateTimeOffset? GuidedAt { get; init; }
    public int GuidedChanges { get; init; }
    // Player profile (first-run welcome): from Steam or the player's own name and picture, PC goal and accent color.
    public bool Onboarded { get; init; }
    public string ProfileSource { get; init; } = "none"; // steam | custom | none
    public string DisplayName { get; init; } = "";
    public string? AvatarPath { get; init; }
    public string Goal { get; init; } = "balanced"; // fps | competitive | balanced
    public string? AdaptiveSync { get; init; } // FreeSync/G-Sync on: "on" | "off" | null (not asked yet; asked before the first game analysis)
    public string Accent { get; init; } = "rose"; // rose | violet | blue | green (design) and the older crimson | gold | dark
    public static readonly string[] Goals = ["fps", "competitive", "balanced"];
    public static readonly string[] Accents = ["rose", "violet", "blue", "green", "crimson", "gold", "dark"];
    // Midnight = dark (stored name kept for older files), Light, System (follows Windows), Graphite = dark on neutral surfaces.
    public static readonly string[] Themes = ["Midnight", "Light", "System", "Graphite"];
    public InterfacePreferences Normalize() => this with
    {
        SchemaVersion = 1,
        Language = Language == "en-US" ? "en-US" : "pt-BR",
        Theme = Themes.Contains(Theme) ? Theme : "Midnight",
        ProfileSource = ProfileSource is "steam" or "custom" ? ProfileSource : "none",
        DisplayName = (DisplayName ?? "").Trim() is var n && n.Length > 32 ? n[..32] : (DisplayName ?? "").Trim(),
        Goal = Goals.Contains(Goal) ? Goal : "balanced",
        AdaptiveSync = AdaptiveSync is "on" or "off" ? AdaptiveSync : null,
        Accent = Accents.Contains(Accent) ? Accent : "rose",
        UiSoundVolume = Math.Clamp(UiSoundVolume, 0, 100)
    };
}

public sealed record PreferencesLoadResult(InterfacePreferences Value, string Status);
public interface IPreferencesStore
{
    PreferencesLoadResult Load();
    bool TrySave(InterfacePreferences preferences);
}

public sealed class PreferencesService : ObservableObject
{
    private readonly IPreferencesStore store;
    private InterfacePreferences current;
    private string? errorKey;
    public InterfacePreferences Current => current;
    public string LoadStatus { get; }
    public string? ErrorKey { get => errorKey; private set => SetProperty(ref errorKey, value); }
    public PreferencesService(IPreferencesStore store)
    {
        this.store = store;
        var loaded = store.Load();
        current = loaded.Value.Normalize();
        LoadStatus = loaded.Status;
    }
    public bool Update(InterfacePreferences value)
    {
        value = value.Normalize();
        if (value == current) { ErrorKey = null; return true; }
        if (!store.TrySave(value)) { ErrorKey = "SaveFailed"; return false; }
        current = value;
        ErrorKey = null;
        OnPropertyChanged(nameof(Current));
        return true;
    }
}

public sealed class PreferencesViewModel : ObservableObject
{
    private readonly PreferencesService service;
    private readonly ITextCatalog text;
    private bool synchronizing;
    private int languageIndex;
    private int themeIndex;
    private bool reduceMotion;
    private string message = "";
    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "Português (Brasil)", "English (United States)" };
    public IReadOnlyList<string> ThemeOptions => [.. InterfacePreferences.Themes.Select(t => text[t])];
    public int LanguageIndex { get => languageIndex; set { if (value is >= 0 and <= 1 && SetProperty(ref languageIndex, value) && !synchronizing) Save(); } }
    public int ThemeIndex { get => themeIndex; set { if (value >= 0 && value < InterfacePreferences.Themes.Length && SetProperty(ref themeIndex, value) && !synchronizing) Save(); } }
    // Accent color (Settings › Appearance and the welcome): applied live, kept when switching light/dark.
    public string Accent { get => service.Current.Accent; set { if (value is not null && value != service.Current.Accent) { if (!service.Update(service.Current with { Accent = value })) Message = text["SaveFailed"]; OnPropertyChanged(); } } }
    public bool ReduceMotion { get => reduceMotion; set { if (SetProperty(ref reduceMotion, value) && !synchronizing) Save(); } }
    public bool StartWithWindows { get => service.Current.StartWithWindows; set { if (value != service.Current.StartWithWindows) { service.Update(service.Current with { StartWithWindows = value }); OnPropertyChanged(); } } }
    public bool CloseToTray { get => service.Current.CloseToTray; set { if (value != service.Current.CloseToTray) { service.Update(service.Current with { CloseToTray = value }); OnPropertyChanged(); } } }
    public bool UiSounds { get => service.Current.UiSounds; set { if (value != service.Current.UiSounds) { service.Update(service.Current with { UiSounds = value }); OnPropertyChanged(); } } }
    public double UiSoundVolume { get => service.Current.UiSoundVolume; set { var v = (int)Math.Round(value); if (v != service.Current.UiSoundVolume) { service.Update(service.Current with { UiSoundVolume = v }); OnPropertyChanged(); } } }
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public ITextCatalog Text => text;
    public string PlayerName => service.Current.DisplayName;
    public PreferencesViewModel(PreferencesService service, ITextCatalog text)
    {
        this.service = service;
        this.text = text;
        languageIndex = service.Current.Language == "en-US" ? 1 : 0;
        themeIndex = Math.Max(0, Array.IndexOf(InterfacePreferences.Themes, service.Current.Theme));
        reduceMotion = service.Current.ReduceMotion;
    }
    private void Save()
    {
        var updated = service.Current with
        {
            Language = LanguageIndex == 1 ? "en-US" : "pt-BR",
            Theme = InterfacePreferences.Themes[ThemeIndex],
            ReduceMotion = ReduceMotion
        };
        if (!service.Update(updated))
        {
            synchronizing = true;
            LanguageIndex = service.Current.Language == "en-US" ? 1 : 0;
            ThemeIndex = Math.Max(0, Array.IndexOf(InterfacePreferences.Themes, service.Current.Theme));
            ReduceMotion = service.Current.ReduceMotion;
            synchronizing = false;
            Message = text["SaveFailed"];
            return;
        }
        Message = text["Saved"];
        OnPropertyChanged(nameof(ThemeOptions));
        OnPropertyChanged(nameof(ThemeIndex));
    }
}
