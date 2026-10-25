using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core.Games;

// Each card carries its own labels and commands, so item templates never depend on the parent DataContext.
public sealed record GameCard(GameEntry Game, string Name, string Store, string Initials, string? CoverPath, bool CanRemove,
    string PlayLabel, string HideLabel, System.Windows.Input.ICommand Launch, System.Windows.Input.ICommand Hide);
public sealed record StoreFilter(string Id, string Label);

// "Meus Jogos": scans launchers in the background, filters by text and store, launches on request.
public sealed class GamesViewModel : ObservableObject
{
    private readonly IGameLibrary library;
    private readonly IManualGameStore manualStore;
    private readonly IGameLauncher launcher;
    private readonly Action<Exception> log;
    private List<GameEntry> all = [];
    private List<GameEntry> manual = [];
    private HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
    private string search = "", store = "", status = "";
    private bool busy, loaded;
    public ITextCatalog Text { get; }
    public ObservableCollection<GameCard> Games { get; } = [];
    public ObservableCollection<StoreFilter> Stores { get; } = [];
    public string Status => status;
    public bool IsBusy => busy;
    public bool IsEmpty => !busy && loaded && Games.Count == 0;
    public string EmptyMessage => !Visible.Any() ? Text["GamesNoneFound"] : Text["GamesNoMatch"];
    public string Summary => Text.Format("GamesSummary", Visible.Count(), Visible.Select(g => g.Store).Distinct().Count());
    private IEnumerable<GameEntry> Visible => all.Where(g => !hidden.Contains(g.Key));
    public string Search { get => search; set { if (SetProperty(ref search, value ?? "")) ApplyFilter(); } }
    public StoreFilter? SelectedStore
    {
        get => Stores.FirstOrDefault(s => s.Id == store);
        set { var id = value?.Id ?? ""; if (store != id) { store = id; OnPropertyChanged(); ApplyFilter(); } }
    }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand<GameCard> LaunchCommand { get; }
    public IRelayCommand<GameCard> RemoveCommand { get; }
    public IRelayCommand<GameCard> HideCommand { get; }
    public IRelayCommand ShowHiddenCommand { get; }
    public int HiddenCount => hidden.Count;
    public bool HasHidden => hidden.Count > 0;
    public string HiddenLabel => Text.Format("GamesShowHidden", hidden.Count);

    public GamesViewModel(IGameLibrary library, IManualGameStore manualStore, IGameLauncher launcher, ITextCatalog text, Action<Exception> log)
    {
        this.library = library; this.manualStore = manualStore; this.launcher = launcher; Text = text; this.log = log;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !busy);
        LaunchCommand = new RelayCommand<GameCard>(Launch, c => c?.Game.CanLaunch == true);
        RemoveCommand = new RelayCommand<GameCard>(Remove, c => c?.CanRemove == true);
        HideCommand = new RelayCommand<GameCard>(Hide);
        ShowHiddenCommand = new RelayCommand(ShowHidden);
    }

    public Task EnsureLoadedAsync() => loaded || busy ? Task.CompletedTask : RefreshAsync();

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; Notify();
        try
        {
            var result = await Task.Run(() => { manual = manualStore.Load().ToList(); hidden = new(manualStore.LoadHidden(), StringComparer.OrdinalIgnoreCase); return library.Scan(manual); });
            all = result.Games.ToList();
            foreach (var e in result.Errors) log(new InvalidOperationException("Game scan: " + e));
            status = "";
        }
        catch (Exception ex) { log(ex); status = Text["GamesScanFailed"]; }
        finally { busy = false; loaded = true; BuildStores(); ApplyFilter(); Notify(); }
    }

    // Manual games are identified by their executable path; adding the same path twice updates the name.
    public async Task<bool> AddManualAsync(string exePath, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exePath))
        { status = Text["GamesInvalidExe"]; Notify(); return false; }
        var full = Path.GetFullPath(exePath);
        var entry = new GameEntry(GameStores.Manual, full.ToLowerInvariant(), string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(full) : name.Trim(),
            Path.GetDirectoryName(full), full);
        manual.RemoveAll(g => g.Id == entry.Id); manual.Add(entry);
        try { await Task.Run(() => manualStore.Save(manual)); }
        catch (Exception ex) { log(ex); status = Text["GamesSaveFailed"]; Notify(); return false; }
        status = Text.Format("GamesAdded", entry.Name);
        await RefreshAsync();
        return true;
    }

    private void Remove(GameCard? card)
    {
        if (card is null || !card.CanRemove) return;
        manual.RemoveAll(g => g.Id == card.Game.Id);
        try { manualStore.Save(manual); all.RemoveAll(g => g.Key == card.Game.Key); status = Text.Format("GamesRemoved", card.Name); }
        catch (Exception ex) { log(ex); status = Text["GamesSaveFailed"]; }
        BuildStores(); ApplyFilter(); Notify();
    }

    private void Hide(GameCard? card)
    {
        if (card is null) return;
        if (card.CanRemove) { Remove(card); return; }
        hidden.Add(card.Game.Key);
        try { manualStore.SaveHidden(hidden.ToList()); status = Text.Format("GamesHidden", card.Name); }
        catch (Exception ex) { log(ex); status = Text["GamesSaveFailed"]; }
        BuildStores(); ApplyFilter(); Notify();
    }
    private void ShowHidden()
    {
        if (hidden.Count == 0) return;
        hidden.Clear();
        try { manualStore.SaveHidden([]); } catch (Exception ex) { log(ex); }
        BuildStores(); ApplyFilter(); Notify();
    }

    private void Launch(GameCard? card)
    {
        if (card is null) return;
        try { launcher.Launch(card.Game); status = Text.Format("GamesLaunching", card.Name); }
        catch (Exception ex) { log(ex); status = Text.Format("GamesLaunchFailed", card.Name); }
        Notify();
    }

    private void BuildStores()
    {
        var current = store;
        Stores.Clear(); Stores.Add(new("", Text["GamesAllStores"]));
        foreach (var s in GameStores.All.Where(s => all.Any(g => g.Store == s && !hidden.Contains(g.Key)))) Stores.Add(new(s, s));
        if (Stores.All(s => s.Id != current)) store = "";
        OnPropertyChanged(nameof(SelectedStore));
    }

    // Accent- and case-insensitive name search, combined with the store filter.
    private void ApplyFilter()
    {
        var term = Normalize(search);
        Games.Clear();
        foreach (var g in all.Where(g => !hidden.Contains(g.Key) && (store.Length == 0 || g.Store == store) && (term.Length == 0 || Normalize(g.Name).Contains(term)))
                              .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            Games.Add(new(g, g.Name, g.Store, Initials(g.Name), g.CoverPath, g.IsManual, Text["GamesPlay"], Text[g.IsManual ? "GamesRemove" : "GamesHide"], LaunchCommand, HideCommand));
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyMessage));
    }

    public static string Normalize(string value)
    {
        var d = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant().Trim();
    }
    public static string Initials(string name)
    {
        var words = name.Split([' ', ':', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToArray();
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    public void RefreshLanguage() { BuildStores(); ApplyFilter(); Notify(); }
    private void Notify()
    {
        foreach (var p in new[] { nameof(Status), nameof(IsBusy), nameof(IsEmpty), nameof(EmptyMessage), nameof(Summary), nameof(HiddenCount), nameof(HiddenLabel), nameof(HasHidden) }) OnPropertyChanged(p);
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
