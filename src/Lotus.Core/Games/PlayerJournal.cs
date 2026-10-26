using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Games;

// The player's own corner: favorite games and a short note per game ("stopped at the 3rd boss", "play with Ana").
// Stored only on this PC. Favorites also lead Home's "Your games" row.
public sealed record PlayerJournalData(HashSet<string> Favorites, Dictionary<string, string> Notes)
{
    public static PlayerJournalData Empty => new([], []);
}

public interface IPlayerJournalStore { PlayerJournalData Load(); void Save(PlayerJournalData data); }

public sealed class PlayerJournal(IPlayerJournalStore store, Action<Exception> log)
{
    public const int NoteLimit = 140;
    private PlayerJournalData data = Safe(store, log);
    public event EventHandler? Changed;

    public bool IsFavorite(string key) => data.Favorites.Contains(key);
    public string Note(string key) => data.Notes.GetValueOrDefault(key, "");
    public int FavoriteCount => data.Favorites.Count;

    public void SetFavorite(string key, bool on)
    {
        if (on ? !data.Favorites.Add(key) : !data.Favorites.Remove(key)) return;
        Persist();
    }
    public void SetNote(string key, string? text)
    {
        var clean = (text ?? "").Trim().ReplaceLineEndings(" ");
        if (clean.Length > NoteLimit) clean = clean[..NoteLimit];
        if (clean == Note(key)) return;
        if (clean.Length == 0) data.Notes.Remove(key); else data.Notes[key] = clean;
        Persist();
    }

    private void Persist()
    {
        try { store.Save(data); } catch (Exception ex) { log(ex); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private static PlayerJournalData Safe(IPlayerJournalStore store, Action<Exception> log)
    {
        try { return store.Load(); } catch (Exception ex) { log(ex); return PlayerJournalData.Empty; }
    }
}

public sealed partial class PlayerGameRow(GameEntry game, PlayerJournal journal, string when) : ObservableObject
{
    public GameEntry Game { get; } = game;
    public string Name => Game.Name;
    public string? Cover => Game.CoverPath;
    public string When { get; } = when;
    public bool Favorite { get => journal.IsFavorite(Game.Key); set { journal.SetFavorite(Game.Key, value); OnPropertyChanged(); OnPropertyChanged(nameof(StarGlyph)); } }
    public string StarGlyph => Favorite ? "" : "";
    public string Note { get => journal.Note(Game.Key); set { journal.SetNote(Game.Key, value); OnPropertyChanged(); OnPropertyChanged(nameof(HasNote)); } }
    public bool HasNote => Note.Length > 0;
}

// Side panel opened from the profile badge.
public sealed class PlayerPanelViewModel(GamesViewModel games, PlayerJournal journal, ITextCatalog text, Func<IReadOnlyDictionary<string, int>>? playtime = null) : ObservableObject
{
    private IReadOnlyDictionary<string, int> minutes = new Dictionary<string, int>();
    public string TotalHours => minutes.Count == 0 ? "" : Text.Format("PpTotal", Hours(minutes.Values.Sum()));
    public bool HasTotal => minutes.Count > 0;
    private static string Hours(int m) => m < 60 ? m + " min" : (m / 60.0).ToString(m < 600 ? "0.#" : "0") + " h";
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<PlayerGameRow> Rows { get; } = [];
    public PlayerGameRow? Last { get; private set; }
    public bool HasLast => Last is not null;
    public bool HasGames => Rows.Count > 0;
    public string Stats => Text.Format("PpStats", Rows.Count, journal.FavoriteCount);

    public void Refresh()
    {
        Rows.Clear();
        try { minutes = playtime?.Invoke() ?? new Dictionary<string, int>(); } catch (IOException) { minutes = new Dictionary<string, int>(); }
        var list = games.InstalledGames();
        // Favorites first, then most recently played, then by name.
        foreach (var g in list.OrderByDescending(g => journal.IsFavorite(g.Key)).ThenByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            Rows.Add(new(g, journal, Played(g)));
        var last = list.Where(g => g.LastPlayed is not null).MaxBy(g => g.LastPlayed);
        Last = last is null ? null : Rows.FirstOrDefault(r => r.Game.Key == last.Key);
        foreach (var n in new[] { nameof(Last), nameof(HasLast), nameof(HasGames), nameof(Stats), nameof(TotalHours), nameof(HasTotal) }) OnPropertyChanged(n);
    }
    public void Play(PlayerGameRow row) => games.Play(row.Game);
    public void NotifyStats() => OnPropertyChanged(nameof(Stats));

    // "33 h · Jogado hoje" — hours from Steam when known.
    private string Played(GameEntry g) => g.Store == GameStores.Steam && minutes.TryGetValue(g.Id, out var m) ? Hours(m) + " · " + When(g.LastPlayed) : When(g.LastPlayed);
    private string When(DateTimeOffset? at)
    {
        if (at is not { } d) return Text["PpNeverPlayed"];
        var days = (DateTimeOffset.Now.Date - d.ToLocalTime().Date).Days;
        return days <= 0 ? Text["HcToday"] : days == 1 ? Text["HcYesterday"] : Text.Format("HcDaysAgo", days);
    }
}
