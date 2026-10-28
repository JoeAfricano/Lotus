using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core;

public sealed record Section(string Id, string Title, string Glyph, string Description, string NextStep);

public static class Sections
{
    public static IReadOnlyList<Section> All { get; } = Create(new TextCatalog());
    public static IReadOnlyList<Section> Create(ITextCatalog text) => Array.AsReadOnly(Definitions.Select(item =>
        new Section(item.Id, text[item.Id + "Title"], item.Glyph, text[item.Id + "Description"], text[item.Id + "Next"])).ToArray());
    private static IEnumerable<(string Id, string Glyph)> Definitions => new[]
    {
        ("home", "\uE80F"), ("optimize", "\uE945"), ("games", "\uE7FC"), ("gamelist", "\uE721"), ("diary", "\uE736"), ("lotusscore", "\uEC4A"), ("tools", "\uE90F"),
        ("performance", "\uE9D2"), ("settings", "\uE713")
    };
}

public interface IAppEvents { void Navigated(string sectionId); }

// Legacy destinations remain addressable; only five groups appear in the sidebar.
public sealed record AppRoute(string Id, string Group);
public static class AppRoutes
{
    public static AppRoute? Resolve(string? id) => id switch
    {
        "home" or "dashboard" => new("home", "home"),
        "optimize" or "optimizations" or "analysis" => new("analysis", "optimize"),
        "advice" => new("analysis", "optimize"), "restore" => new("restore", "optimize"),
        "games" => new("games", "games"), "gamelist" => new("gamelist", "gamelist"), "diary" => new("diary", "diary"), "score" or "lotusscore" => new("lotusscore", "lotusscore"), "tools" => new("tools", "tools"),
        "performance" or "monitoring" => new("monitoring", "performance"),
        "benchmark" => new("benchmark", "performance"), "history" => new("history", "performance"),
        "settings" or "settings-monitoring" => new("settings", "settings"),
        "settings-integrations" => new("settings-integrations", "settings"),
        "settings-security" => new("settings-security", "settings"), "logs" => new("logs", "settings"), "faq" => new("faq", "settings"), "backup" => new("backup", "settings"),
        _ => null
    };
}

public sealed class ShellViewModel : ObservableObject
{
    private readonly IAppEvents events;
    private Section selectedSection;
    public string CurrentRoute { get; private set; } = "home";
    public ITextCatalog Text { get; }
    public IReadOnlyList<Section> Items { get; private set; }
    public Section SelectedSection => selectedSection;
    public IRelayCommand<string> NavigateCommand { get; }
    public int PerformanceTabIndex => CurrentRoute == "benchmark" ? 1 : CurrentRoute == "history" ? 2 : 0;
    public int OptimizeTabIndex => CurrentRoute == "restore" ? 1 : 0;
    public int SettingsCategoryIndex
    {
        get => CurrentRoute switch { "settings-integrations" => 1, "settings-security" => 2, "logs" => 3, "faq" => 4, "backup" => 5, _ => 0 };
        set { if (value is >= 0 and <= 5) Navigate(new[] { "settings", "settings-integrations", "settings-security", "logs", "faq", "backup" }[value]); }
    }
    public IReadOnlyList<string> SettingsCategories => new[] { "AppearanceCategory", "IntegrationsCategory", "SafetyCategory", "AdvancedCategory", "FaqCategory", "BackupCategory" }.Select(key => Text[key]).ToArray();
    public ShellViewModel(IAppEvents events, ITextCatalog? text = null)
    {
        this.events = events; Text = text ?? new TextCatalog();
        Items = Sections.Create(Text); selectedSection = Items[0];
        NavigateCommand = new RelayCommand<string>(Navigate);
    }
    public void RefreshLanguage()
    {
        var group = selectedSection.Id; Items = Sections.Create(Text);
        selectedSection = Items.Single(section => section.Id == group);
        NotifyNavigation(); OnPropertyChanged(nameof(Items)); OnPropertyChanged(nameof(SettingsCategories));
    }
    private void Navigate(string? id)
    {
        var route = AppRoutes.Resolve(id);
        if (route is null || route.Id == CurrentRoute) return;
        CurrentRoute = route.Id; selectedSection = Items.Single(section => section.Id == route.Group);
        NotifyNavigation(); events.Navigated(CurrentRoute);
    }
    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CurrentRoute)); OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(PerformanceTabIndex)); OnPropertyChanged(nameof(OptimizeTabIndex)); OnPropertyChanged(nameof(SettingsCategoryIndex));
    }
}


public readonly record struct MonitoringDemand(bool Collect, bool Analyze)
{
    public static MonitoringDemand For(string route, bool visible, bool analysisRequested) =>
        new(visible && (route is "monitoring" or "home" || analysisRequested), visible && analysisRequested); // Home shows live PC readiness
}
