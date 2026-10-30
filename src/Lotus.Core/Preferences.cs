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
    public string DiaryView { get; init; } = "grid"; // diary layout: grid | list | table
    public bool DiaryHeaderCompact { get; init; } = true; // diary profile header: compact band or the full layout
    public bool AutoSessions { get; init; } = true; // record play sessions (time, dates) in the diary while a library game is open
    public bool GamePriority { get; init; } // while a game is open: game above normal, known launchers/browsers below normal (restored after)
    public string? AdaptiveSync { get; init; } // FreeSync/G-Sync on: "on" | "off" | null (not asked yet; asked before the first game analysis)
    public string Accent { get; init; } = "rose"; // rose | violet | blue | green (design) and the older crimson | gold | dark
    public string? CustomTheme { get; init; }                 // the active custom theme (its share code), used when Theme is "Custom"
    // Themes the player saved (share codes, newest first), kept as one text with a code per line so preferences keep
    // comparing by value (a list would only equal itself).
    public string SavedThemes { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> MyThemes => SplitThemes(SavedThemes);
    public static string JoinThemes(IEnumerable<string> codes) => string.Join('\n', codes);
    private static string[] SplitThemes(string? text) => (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public const int MyThemesLimit = 24;
    public Lotus.Core.Themes.ThemeSpec? ActiveCustom() => Theme == "Custom" && Lotus.Core.Themes.ThemeEngine.TryFromCode(CustomTheme, out var s) ? s : null;
    public static readonly string[] Goals = ["fps", "competitive", "balanced"];
    public static readonly string[] Accents = ["rose", "violet", "blue", "green", "crimson", "gold", "dark"];
    // Midnight = dark (stored name kept for older files), Light, System (follows Windows), Graphite = dark on neutral surfaces.
    public static readonly string[] Themes = ["Midnight", "Light", "System", "Graphite"];
    public InterfacePreferences Normalize() => this with
    {
        SchemaVersion = 1,
        Language = Language == "en-US" ? "en-US" : "pt-BR",
        Theme = Themes.Contains(Theme) || (Theme == "Custom" && Lotus.Core.Themes.ThemeEngine.TryFromCode(CustomTheme, out _)) ? Theme : "Midnight",
        SavedThemes = JoinThemes(SplitThemes(SavedThemes).Where(c => Lotus.Core.Themes.ThemeEngine.TryFromCode(c, out _)).Distinct().Take(MyThemesLimit)),
        ProfileSource = ProfileSource is "steam" or "custom" ? ProfileSource : "none",
        DisplayName = (DisplayName ?? "").Trim() is var n && n.Length > 32 ? n[..32] : (DisplayName ?? "").Trim(),
        Goal = Goals.Contains(Goal) ? Goal : "balanced",
        AdaptiveSync = AdaptiveSync is "on" or "off" ? AdaptiveSync : null,
        DiaryView = DiaryView is "grid" or "list" or "table" ? DiaryView : "grid",
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
    public bool AutoSessions { get => service.Current.AutoSessions; set { if (value != service.Current.AutoSessions) { service.Update(service.Current with { AutoSessions = value }); OnPropertyChanged(); } } }
    public bool GamePriority { get => service.Current.GamePriority; set { if (value != service.Current.GamePriority) { service.Update(service.Current with { GamePriority = value }); OnPropertyChanged(); } } }
    public double UiSoundVolume { get => service.Current.UiSoundVolume; set { var v = (int)Math.Round(value); if (v != service.Current.UiSoundVolume) { service.Update(service.Current with { UiSoundVolume = v }); OnPropertyChanged(); } } }
    public string Message { get => message; private set => SetProperty(ref message, value); }

    // ---- Custom themes (Theme Studio, gallery, shared codes) ----
    public bool IsCustom => service.Current.Theme == "Custom";
    public string? CustomCode => IsCustom ? service.Current.CustomTheme : null;
    public Themes.ThemeSpec? ActiveCustom => CustomCode is { } c && Themes.ThemeEngine.TryFromCode(c, out var s) ? s : null;
    public IReadOnlyList<string> MyThemes => service.Current.MyThemes;
    // Uses a custom theme now (gallery, studio, pasted code). The built-in mode cards show nothing selected meanwhile.
    public bool UseTheme(Themes.ThemeSpec spec)
    {
        var code = Themes.ThemeEngine.ToCode(spec);
        if (!service.Update(service.Current with { Theme = "Custom", CustomTheme = code })) { Message = text["SaveFailed"]; return false; }
        themeIndex = -1;
        foreach (var n in new[] { nameof(ThemeIndex), nameof(IsCustom), nameof(CustomCode), nameof(ActiveCustom) }) OnPropertyChanged(n);
        return true;
    }
    // Saves to "Meus temas" (a theme with the same name is replaced) and puts it first.
    public bool SaveMyTheme(Themes.ThemeSpec spec)
    {
        var clean = Themes.ThemeEngine.Normalize(spec);
        var code = Themes.ThemeEngine.ToCode(clean);
        var others = service.Current.MyThemes.Where(c => !(Themes.ThemeEngine.TryFromCode(c, out var s) && string.Equals(s.Name, clean.Name, StringComparison.CurrentCultureIgnoreCase)));
        if (!service.Update(service.Current with { SavedThemes = InterfacePreferences.JoinThemes([code, .. others.Take(InterfacePreferences.MyThemesLimit - 1)]) })) { Message = text["SaveFailed"]; return false; }
        OnPropertyChanged(nameof(MyThemes));
        return true;
    }
    public void DeleteMyTheme(string code)
    {
        if (service.Update(service.Current with { SavedThemes = InterfacePreferences.JoinThemes(service.Current.MyThemes.Where(c => c != code)) })) OnPropertyChanged(nameof(MyThemes));
    }
    public ITextCatalog Text => text;
    public string PlayerName => service.Current.DisplayName;
    public PreferencesViewModel(PreferencesService service, ITextCatalog text)
    {
        this.service = service;
        this.text = text;
        languageIndex = service.Current.Language == "en-US" ? 1 : 0;
        themeIndex = Array.IndexOf(InterfacePreferences.Themes, service.Current.Theme); // -1 while a custom theme is in use
        reduceMotion = service.Current.ReduceMotion;
    }
    private void Save()
    {
        var updated = service.Current with
        {
            Language = LanguageIndex == 1 ? "en-US" : "pt-BR",
            Theme = ThemeIndex >= 0 ? InterfacePreferences.Themes[ThemeIndex] : service.Current.Theme,
            ReduceMotion = ReduceMotion
        };
        if (!service.Update(updated))
        {
            synchronizing = true;
            LanguageIndex = service.Current.Language == "en-US" ? 1 : 0;
            themeIndex = Array.IndexOf(InterfacePreferences.Themes, service.Current.Theme); OnPropertyChanged(nameof(ThemeIndex));
            ReduceMotion = service.Current.ReduceMotion;
            synchronizing = false;
            Message = text["SaveFailed"];
            return;
        }
        Message = text["Saved"];
        OnPropertyChanged(nameof(ThemeOptions));
        OnPropertyChanged(nameof(ThemeIndex)); OnPropertyChanged(nameof(IsCustom)); OnPropertyChanged(nameof(ActiveCustom));
    }
}
