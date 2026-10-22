using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core;

public sealed record InterfacePreferences
{
    public int SchemaVersion { get; init; } = 1;
    public string Language { get; init; } = "pt-BR";
    public string Theme { get; init; } = "Midnight";
    public bool SidebarExpanded { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public InterfacePreferences Normalize() => this with
    {
        SchemaVersion = 1,
        Language = Language == "en-US" ? "en-US" : "pt-BR",
        Theme = Theme == "Graphite" ? "Graphite" : "Midnight"
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
    public IReadOnlyList<string> ThemeOptions => new[] { text["Midnight"], text["Graphite"] };
    public int LanguageIndex { get => languageIndex; set { if (value is >= 0 and <= 1 && SetProperty(ref languageIndex, value) && !synchronizing) Save(); } }
    public int ThemeIndex { get => themeIndex; set { if (value is >= 0 and <= 1 && SetProperty(ref themeIndex, value) && !synchronizing) Save(); } }
    public bool ReduceMotion { get => reduceMotion; set { if (SetProperty(ref reduceMotion, value) && !synchronizing) Save(); } }
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public PreferencesViewModel(PreferencesService service, ITextCatalog text)
    {
        this.service = service;
        this.text = text;
        languageIndex = service.Current.Language == "en-US" ? 1 : 0;
        themeIndex = service.Current.Theme == "Graphite" ? 1 : 0;
        reduceMotion = service.Current.ReduceMotion;
    }
    private void Save()
    {
        var updated = service.Current with
        {
            Language = LanguageIndex == 1 ? "en-US" : "pt-BR",
            Theme = ThemeIndex == 1 ? "Graphite" : "Midnight",
            ReduceMotion = ReduceMotion
        };
        if (!service.Update(updated))
        {
            synchronizing = true;
            LanguageIndex = service.Current.Language == "en-US" ? 1 : 0;
            ThemeIndex = service.Current.Theme == "Graphite" ? 1 : 0;
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
