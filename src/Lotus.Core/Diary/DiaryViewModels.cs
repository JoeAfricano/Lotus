using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Diary;

public sealed record DiaryTab(string Id, string Label, int Count);
public sealed record BadgeView(string Id, string Glyph, string Name, string Detail, bool Unlocked, string ProgressText, double Fraction);
public sealed record FeatChip(string Id, string Glyph, string Label);
public sealed record ShowcaseSlot(int Index, DiaryCard? Card) { public bool IsEmpty => Card is null; public bool HasCard => Card is not null; }

// One game in the diary list.
public sealed class DiaryCard(DiaryEntry e, ITextCatalog t, int rank)
{
    public DiaryEntry Entry { get; } = e;
    public string Key => Entry.Key;
    public string Name => Entry.Name;
    public string? Cover => Entry.Cover;
    public string Rank { get; } = rank.ToString(CultureInfo.InvariantCulture);
    public string Meta => string.Join(" · ", new[] { string.Join(", ", Entry.Genres.Take(2)), Entry.Finished?.Year.ToString(CultureInfo.InvariantCulture) ?? Entry.Started?.Year.ToString(CultureInfo.InvariantCulture) ?? "" }.Where(s => s.Length > 0));
    public string DateText => Entry.Finished is { } f ? t.Format("DyFinishedOn", f.ToString("d", Culture(t))) : t.Format("DyAddedOn", Entry.Added.LocalDateTime.ToString("d", Culture(t)));
    public string Status => t["DyStatus" + Entry.Status];
    public string StatusKey => Entry.Status.ToString();
    public string Rating => Entry.Rating is { } r ? r.ToString(r % 1 == 0 ? "0" : "0.0", Culture(t)) : "—";
    public string RatingKind => DiaryText.RatingKind(Entry.Rating);
    public bool HasHours => Entry.Minutes > 0;
    public string Hours => DiaryText.Hours(Entry.Minutes);
    public string Platform => Entry.Platform;
    public IReadOnlyList<FeatChip> Feats { get; } = [.. e.Feats.Select(f => new FeatChip(f, global::Lotus.Core.Diary.Feats.All.First(x => x.Id == f).Glyph, t["DyFeat" + f]))];
    public bool HasFeats => Feats.Count > 0;
    public string Review => Entry.Review;
    public bool HasReview => Entry.Review.Length > 0;
    // Table columns.
    public string StartedText => Entry.Started?.ToString("d", Culture(t)) ?? "—";
    public string FinishedText => Entry.Finished?.ToString("d", Culture(t)) ?? "—";
    public string HoursOrDash => Entry.Minutes > 0 ? DiaryText.Hours(Entry.Minutes) : "—";
    public string Genre => Entry.Genres.FirstOrDefault() ?? "—";
    // Special covers: holographic sheen for platinum, gold seal for a 10, faded art for dropped games.
    public bool IsPlatinum => Entry.Feats.Contains("platinum");
    public bool IsPerfect => Entry.Rating >= 10;
    public bool IsDropped => Entry.Status == DiaryStatus.Dropped;
    public double CoverOpacity => IsDropped ? 0.42 : 1;
    private static CultureInfo Culture(ITextCatalog t) => CultureInfo.GetCultureInfo(t.Language);
}

public static class DiaryText
{
    public static string Hours(int minutes) => minutes < 60 ? minutes + " min" : (minutes / 60.0).ToString(minutes < 600 ? "0.#" : "0", CultureInfo.InvariantCulture) + "h";
    // Same colour bands as review sites: green ≥ 7.5, yellow ≥ 5, red below; none when unrated.
    public static string RatingKind(double? r) => r switch { null => "none", >= 7.5 => "good", >= 5 => "mixed", _ => "bad" };
}

// "Meu diário" page: profile header, progress, badges and the list with status tabs.
public sealed class DiaryViewModel : ObservableObject
{
    private readonly GameDiary diary;
    private string tab = "all", query = "", sort = "recent";
    public DiaryViewModel(GameDiary diary, ITextCatalog text, PreferencesService preferences)
    {
        this.diary = diary; Text = text; Preferences = preferences; Stats = new DiaryStatsViewModel(diary, text); PlayNext = new PlayNextViewModel(diary, text);
        diary.Changed += (_, _) => Refresh();
        diary.ProgressChanged += (_, _) => RefreshProgress();
        Refresh();
    }
    public ITextCatalog Text { get; }
    public PreferencesService Preferences { get; }
    public GameDiary Diary => diary;
    // Achievements (Lotus and Steam), shown in the diary's other sections.
    public Achievements.AchievementsViewModel? Achievements { get; set; }
    private string section = "games";
    public string Section { get => section; set { if (value is "games" or "stats" or "trophies" or "steam") SetProperty(ref section, value); } }
    // Statistics tab (charts) and the yearly recap.
    public DiaryStatsViewModel Stats { get; }
    // "O que jogar agora?" (picks from the player's own list).
    public PlayNextViewModel PlayNext { get; }
    // "Quero jogar" games on sale (set by the window, which owns the store catalog).
    public DiaryDealsViewModel? Deals { get; set; }
    public Progress Progress { get; private set; } = Progression.Of([]);
    public ObservableCollection<DiaryCard> Items { get; } = [];
    public ObservableCollection<DiaryTab> Tabs { get; } = [];
    public IReadOnlyList<BadgeView> Badges { get; private set; } = [];
    public IReadOnlyList<DiaryTab> SortOptions => [.. new[] { "recent", "rating", "hours", "finished", "name" }.Select(s => new DiaryTab(s, Text["DySort" + s], 0))];

    public string Name => Preferences.Current.DisplayName is { Length: > 0 } n ? n : Text["ObDefaultName"];
    public string? Avatar => Preferences.Current.AvatarPath;
    public string Bio { get => diary.Bio; set { diary.SetBio(value); OnPropertyChanged(); } }
    public string LevelText => Text.Format("DyLevel", Progress.Level);
    public string TitleText => Text[Progress.TitleKey];
    public string XpText => Text.Format("DyXp", Progress.Xp - Progress.LevelStartXp, Progress.NextLevelXp - Progress.LevelStartXp);
    public double LevelPercent => Progress.LevelFraction * 100;
    public string StatCompleted => Progress.Completed.ToString(CultureInfo.InvariantCulture);
    public string StatHours => DiaryText.Hours(Progress.TotalMinutes);
    public string StatAverage => Progress.AverageRating?.ToString("0.0", CultureInfo.GetCultureInfo(Text.Language)) ?? "—";
    public string StatYear => Progress.CompletedThisYear.ToString(CultureInfo.InvariantCulture);
    public string BadgesText => Text.Format("DyBadgesCount", Progress.UnlockedBadges, Progress.Badges.Count);
    public bool IsEmpty => diary.Entries.Count == 0;
    public bool NoMatch => !IsEmpty && Items.Count == 0;

    public string Tab { get => tab; set { if (value is not null && SetProperty(ref tab, value)) Fill(); } }
    public string Query { get => query; set { if (SetProperty(ref query, value ?? "")) Fill(); } }
    public string Sort { get => sort; set { if (value is not null && SetProperty(ref sort, value)) Fill(); } }


    // ---- "Top 4" showcase ----
    public ObservableCollection<ShowcaseSlot> Showcase { get; } = [];
    private void BuildShowcase()
    {
        Showcase.Clear();
        for (var i = 0; i < GameDiary.ShowcaseSize; i++)
        {
            var key = i < diary.Showcase.Count ? diary.Showcase[i] : "";
            Showcase.Add(new(i, key.Length > 0 && diary.Get(key) is { } e ? new DiaryCard(e, Text, i + 1) : null));
        }
    }
    public void SetShowcase(int slot, string? key) => diary.SetShowcase(slot, key);
    // Candidates for a slot: best rated and most played first.
    public IReadOnlyList<DiaryEntry> ShowcaseCandidates() => [.. diary.Entries.OrderByDescending(e => e.Rating ?? -1).ThenByDescending(e => e.Minutes)];
    // ---- Layout and filters ----
    // Layout: "grid" (cover wall) | "list" (detailed cards) | "table" (one row per game); remembered in preferences.
    public string ViewMode
    {
        get => Preferences.Current.DiaryView;
        set { if (value is "grid" or "list" or "table" && value != Preferences.Current.DiaryView) { Preferences.Update(Preferences.Current with { DiaryView = value }); NotifyView(); } }
    }
    public bool HeaderCompact { get => Preferences.Current.DiaryHeaderCompact; set { if (value != Preferences.Current.DiaryHeaderCompact) { Preferences.Update(Preferences.Current with { DiaryHeaderCompact = value }); OnPropertyChanged(); } } }
    public bool IsGrid => ViewMode == "grid"; public bool IsList => ViewMode == "list"; public bool IsTable => ViewMode == "table";
    private void NotifyView() { foreach (var n in new[] { nameof(ViewMode), nameof(IsGrid), nameof(IsList), nameof(IsTable) }) OnPropertyChanged(n); }

    // Filter values (null = any). Options come from the player's own entries, with counts.
    private string? genre, platform, store, rating, year;
    public string? Genre { get => genre; set { if (SetProperty(ref genre, value)) Fill(); } }
    public string? Platform { get => platform; set { if (SetProperty(ref platform, value)) Fill(); } }
    public string? Store { get => store; set { if (SetProperty(ref store, value)) Fill(); } }
    public string? RatingBand { get => rating; set { if (SetProperty(ref rating, value)) Fill(); } }
    public string? Year { get => year; set { if (SetProperty(ref year, value)) Fill(); } }
    public IReadOnlyList<DiaryTab> GenreOptions { get; private set; } = [];
    public IReadOnlyList<DiaryTab> PlatformOptions { get; private set; } = [];
    public IReadOnlyList<DiaryTab> StoreOptions { get; private set; } = [];
    public IReadOnlyList<DiaryTab> YearOptions { get; private set; } = [];
    public IReadOnlyList<DiaryTab> RatingOptions { get; private set; } = [];
    public static string StoreOf(DiaryEntry e) => e.Key.Split(':')[0].ToLowerInvariant() switch
    {
        "steam" => "Steam", "epic" => "Epic", "gog" => "GOG", "xbox" or "microsoft" or "ms" => "Xbox", "ea" or "origin" => "EA", "ubisoft" or "uplay" => "Ubisoft", "battlenet" or "battle.net" => "Battle.net",
        _ => "Other",
    };
    private static string? YearOf(DiaryEntry e) => (e.Finished ?? e.Started)?.Year.ToString(CultureInfo.InvariantCulture);
    private static bool InBand(double? r, string band) => band switch { "9" => r >= 9, "7.5" => r >= 7.5, "5" => r is >= 5 and < 7.5, "low" => r < 5, "none" => r is null, _ => true };
    private void BuildOptions()
    {
        var all = diary.Entries;
        IReadOnlyList<DiaryTab> Group(IEnumerable<string> values, Func<string, string>? label = null) =>
            [.. values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new DiaryTab(g.Key, label?.Invoke(g.Key) ?? g.Key, g.Count()))];
        GenreOptions = Group(all.SelectMany(e => e.Genres.Take(3)));
        PlatformOptions = Group(all.Select(e => e.Platform));
        StoreOptions = Group(all.Select(StoreOf), s => s == "Other" ? Text["DyStoreOther"] : s);
        YearOptions = [.. all.Select(YearOf).OfType<string>().GroupBy(y => y).OrderByDescending(g => g.Key).Select(g => new DiaryTab(g.Key, g.Key, g.Count()))];
        RatingOptions = [.. new[] { "9", "7.5", "5", "low", "none" }.Select(b => new DiaryTab(b, Text["DyBand" + b.Replace(".", "")], all.Count(e => InBand(e.Rating, b))))];
        foreach (var n in new[] { nameof(GenreOptions), nameof(PlatformOptions), nameof(StoreOptions), nameof(YearOptions), nameof(RatingOptions) }) OnPropertyChanged(n);
    }
    public bool HasFilters => genre is not null || platform is not null || store is not null || rating is not null || year is not null;
    // Filters always shown as text ("Filters: RPG · PC · 9+"), never only as a highlighted chip.
    public string ActiveFilters => !HasFilters ? "" : Text.Format("DyFiltersActive", string.Join(" · ", new[]
    {
        genre, platform, store is null ? null : store == "Other" ? Text["DyStoreOther"] : store, rating is null ? null : Text["DyBand" + rating.Replace(".", "")], year,
    }.OfType<string>()));
    public void ClearFilters()
    {
        genre = platform = store = rating = year = null;
        foreach (var n in new[] { nameof(Genre), nameof(Platform), nameof(Store), nameof(RatingBand), nameof(Year) }) OnPropertyChanged(n);
        Fill();
    }

    // Saved filters (kept in the diary file, so they travel with the backup).
    public IReadOnlyList<DiaryFilter> SavedFilters => diary.Filters;
    public bool HasSavedFilters => diary.Filters.Count > 0;
    public void SaveCurrentFilter(string name) => diary.SaveFilter(new DiaryFilter { Name = name, Tab = tab, Genre = genre, Platform = platform, Store = store, Rating = rating, Year = year, Sort = sort });
    public void ApplyFilter(DiaryFilter f)
    {
        tab = f.Tab; genre = f.Genre; platform = f.Platform; store = f.Store; rating = f.Rating; year = f.Year; sort = f.Sort;
        foreach (var n in new[] { nameof(Tab), nameof(Genre), nameof(Platform), nameof(Store), nameof(RatingBand), nameof(Year), nameof(Sort) }) OnPropertyChanged(n);
        Fill();
    }
    public void DeleteFilter(string name) => diary.DeleteFilter(name);

    // Home and the profile panel: what the player is playing now and wants to play next.
    public IReadOnlyList<DiaryCard> Shelf(int max = 8) => [.. diary.Entries
        .Where(e => e.Status is DiaryStatus.Playing or DiaryStatus.Want)
        .OrderBy(e => e.Status == DiaryStatus.Playing ? 0 : 1).ThenByDescending(e => e.Updated).Take(max)
        .Select((e, i) => new DiaryCard(e, Text, i + 1))];

    public void Refresh()
    {
        Progress = diary.Progress;
        var all = diary.Entries;
        Tabs.Clear();
        Tabs.Add(new("all", Text["DyTabAll"], all.Count));
        foreach (var s in new[] { DiaryStatus.Playing, DiaryStatus.Completed, DiaryStatus.Want, DiaryStatus.Paused, DiaryStatus.Dropped })
            Tabs.Add(new(s.ToString(), Text["DyStatus" + s], all.Count(e => e.Status == s)));
        Badges = [.. Progress.Badges.OrderByDescending(b => b.Unlocked).ThenByDescending(b => b.Fraction).Select(b =>
            new BadgeView(b.Id, b.Glyph, Text["DyBadge" + b.Id], Text["DyBadge" + b.Id + "Hint"], b.Unlocked,
                b.Unlocked ? Text["DyBadgeDone"] : $"{b.Current}/{b.Goal}", b.Fraction))];
        BuildOptions(); BuildShowcase(); Stats.Refresh(); PlayNext.Refresh(); Deals?.Invalidate();
        Fill();
        foreach (var p in typeof(DiaryViewModel).GetProperties().Where(p => p.Name is not (nameof(Items) or nameof(Tabs) or nameof(Text) or nameof(Preferences) or nameof(Diary) or nameof(Tab) or nameof(Query) or nameof(Sort) or nameof(Section) or nameof(Achievements) or nameof(Stats) or nameof(PlayNext) or nameof(Deals))))
            OnPropertyChanged(p.Name);
    }

    // Only the level/XP numbers changed (achievements, Steam): no need to rebuild the list.
    public void RefreshProgress()
    {
        Progress = diary.Progress;
        foreach (var n in new[] { nameof(Progress), nameof(LevelText), nameof(TitleText), nameof(XpText), nameof(LevelPercent) }) OnPropertyChanged(n);
    }

    private void Fill()
    {
        IEnumerable<DiaryEntry> rows = diary.Entries;
        if (tab != "all" && Enum.TryParse<DiaryStatus>(tab, out var st)) rows = rows.Where(e => e.Status == st);
        if (query.Trim() is { Length: > 0 } q) rows = rows.Where(e => e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || e.Review.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        if (genre is not null) rows = rows.Where(e => e.Genres.Contains(genre));
        if (platform is not null) rows = rows.Where(e => e.Platform == platform);
        if (store is not null) rows = rows.Where(e => StoreOf(e) == store);
        if (rating is not null) rows = rows.Where(e => InBand(e.Rating, rating));
        if (year is not null) rows = rows.Where(e => YearOf(e) == year);
        rows = sort switch
        {
            "rating" => rows.OrderByDescending(e => e.Rating ?? -1).ThenBy(e => e.Name),
            "hours" => rows.OrderByDescending(e => e.Minutes),
            "name" => rows.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
            "finished" => rows.OrderByDescending(e => e.Finished ?? DateTime.MinValue).ThenByDescending(e => e.Updated),
            _ => rows.OrderByDescending(e => e.Updated),
        };
        Items.Clear();
        var i = 1; foreach (var e in rows) Items.Add(new(e, Text, i++));
        foreach (var n in new[] { nameof(IsEmpty), nameof(NoMatch), nameof(HasFilters), nameof(ActiveFilters), nameof(ResultCount) }) OnPropertyChanged(n);
    }
    public string ResultCount => Text.Format("DyResultCount", Items.Count);
}

public sealed partial class StatusOption(DiaryStatus status, string label, string glyph) : ObservableObject
{
    public DiaryStatus Status { get; } = status;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    [ObservableProperty] private bool selected;
}
public sealed partial class FeatToggle(string id, string glyph, string label) : ObservableObject
{
    public string Id { get; } = id; public string Glyph { get; } = glyph; public string Label { get; } = label;
    [ObservableProperty] private bool selected;
}
// One category score (0 = not scored), shown as a small slider.
public sealed partial class CategoryScore(string id, string label) : ObservableObject
{
    public string Id { get; } = id; public string Label { get; } = label;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValueText))] private double value;
    public string ValueText => Value <= 0 ? "—" : Value.ToString(Value % 1 == 0 ? "0" : "0.0", CultureInfo.CurrentCulture);
}
// One extra playthrough row in the editor.
public sealed partial class RunRow : ObservableObject
{
    [ObservableProperty] private DateTimeOffset? started;
    [ObservableProperty] private DateTimeOffset? finished;
    [ObservableProperty] private double hours;
    [ObservableProperty] private string platform = "PC";
    public Playthrough ToPlaythrough() => new(Started?.Date, Finished?.Date, (int)Math.Round(Math.Max(0, double.IsNaN(Hours) ? 0 : Hours) * 60), Platform);
    // Dates are never null here (an empty CalendarDatePicker bound to null shows a bogus year).
    public static RunRow From(Playthrough p) => new() { Started = p.Started ?? p.Finished ?? DateTime.Today, Finished = p.Finished ?? p.Started ?? DateTime.Today, Hours = Math.Round(p.Minutes / 60.0, 1), Platform = p.Platform };
    public string Key => $"{Started?.Date}|{Finished?.Date}|{Hours}|{Platform}";
}

// The "log this game" sheet: status, score, hours, dates, feats and review.
public sealed partial class DiaryEditorViewModel(GameDiary diary, ITextCatalog text) : ObservableObject
{
    private DiaryEntry entry = new();
    public ITextCatalog Text { get; } = text;
    public IReadOnlyList<StatusOption> Statuses { get; } = [.. new[] { (DiaryStatus.Completed, ""), (DiaryStatus.Playing, ""), (DiaryStatus.Dropped, ""), (DiaryStatus.Want, ""), (DiaryStatus.Paused, "") }
        .Select(s => new StatusOption(s.Item1, text["DyStatus" + s.Item1], s.Item2))];
    public IReadOnlyList<FeatToggle> FeatToggles { get; } = [.. Feats.All.Select(f => new FeatToggle(f.Id, f.Glyph, text["DyFeat" + f.Id]))];
    public IReadOnlyList<string> Platforms { get; } = ["PC", "Steam Deck", "Console", "Mobile"];
    public IReadOnlyList<CategoryScore> Categories { get; } = [.. DiaryOptions.Categories.Select(c => new CategoryScore(c, text["DyCat" + c]))];
    public IReadOnlyList<FeatToggle> Moods { get; } = [.. DiaryOptions.Moods.Select(m => new FeatToggle(m.Id, m.Glyph, text["DyMood" + m.Id]))];
    public IReadOnlyList<DiaryTab> Difficulties { get; } = [new("", text["DyDiffNone"], 0), .. DiaryOptions.Difficulties.Select(d => new DiaryTab(d, text["DyDiff" + d], 0))];
    [ObservableProperty] private string difficulty = "";
    public System.Collections.ObjectModel.ObservableCollection<RunRow> Runs { get; } = [];
    public void AddRun() => Runs.Add(new RunRow { Platform = Platform, Started = DateTimeOffset.Now.Date, Finished = DateTimeOffset.Now.Date });
    public void RemoveRun(RunRow run) => Runs.Remove(run);
    // Average of the categories the player scored (null when none): offered as the overall score.
    public double? CategoryAverage => Categories.Where(c => c.Value > 0).Select(c => c.Value).DefaultIfEmpty(-1).Average() is var a && a > 0 ? Math.Round(a * 2, MidpointRounding.AwayFromZero) / 2 : null;
    public string CategoryAverageText => CategoryAverage is { } a ? Text.Format("DyCatUseAverage", a.ToString(a % 1 == 0 ? "0" : "0.0", CultureInfo.GetCultureInfo(Text.Language))) : "";
    public void UseCategoryAverage() { if (CategoryAverage is { } a) Rating = a; }
    public void NotifyCategories() { OnPropertyChanged(nameof(CategoryAverage)); OnPropertyChanged(nameof(CategoryAverageText)); }
    // Score label shown under the big number (Ruim … Obra-prima).
    public string RatingLabel => Rating switch { <= 0 => Text["DyScoreHint"], < 3 => Text["DyLabelBad"], < 5 => Text["DyLabelWeak"], < 7 => Text["DyLabelOk"], < 8.5 => Text["DyLabelGood"], < 10 => Text["DyLabelGreat"], _ => Text["DyLabelMasterpiece"] };

    public string Name => entry.Name;
    public string? Cover => entry.Cover;
    public string? Wide => entry.Wide;
    public string EntryKey => entry.Key;
    public string? AppId => entry.AppId;
    public bool IsNew { get; private set; }
    public bool CanRemove => !IsNew;
    public int? SteamMinutes { get; private set; }
    public bool HasSteamHours => SteamMinutes is > 0;
    public string SteamHoursLabel => SteamMinutes is { } m ? Text.Format("DyUseSteamHours", DiaryText.Hours(m)) : "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RatingText), nameof(RatingKind), nameof(RatingLabel))] private double rating;
    [ObservableProperty] private double hours;
    [ObservableProperty] private DateTimeOffset? started;
    [ObservableProperty] private DateTimeOffset? finished;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ReviewCount))] private string review = "";
    [ObservableProperty] private string platform = "PC";
    public string RatingText => Rating <= 0 ? Text["DyNoScore"] : Rating.ToString(Rating % 1 == 0 ? "0" : "0.0", CultureInfo.GetCultureInfo(Text.Language));
    public string RatingKind => DiaryText.RatingKind(Rating <= 0 ? null : Rating);
    public string ReviewCount => Text.Format("DyReviewCount", Review.Length, GameDiary.ReviewLimit, Review.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length);
    public DiaryStatus Status => Statuses.FirstOrDefault(s => s.Selected)?.Status ?? DiaryStatus.Want;

    // Opens the sheet for a game: its saved entry when there is one, else a fresh one (pre-set status optional).
    public void Open(DiaryEntry game, int? steamMinutes = null, DiaryStatus? status = null)
    {
        var saved = diary.Get(game.Key);
        IsNew = saved is null;
        entry = (saved ?? game).Clone();
        if (saved is not null) { entry.Cover ??= game.Cover; entry.Wide ??= game.Wide; if (entry.Genres.Count == 0) entry.Genres = [.. game.Genres]; }
        SteamMinutes = steamMinutes;
        SelectStatus(status ?? (IsNew ? DiaryStatus.Playing : entry.Status));
        Rating = entry.Rating ?? 0;
        Hours = Math.Round(entry.Minutes / 60.0, 1);
        if (IsNew && Hours == 0 && steamMinutes is > 0) Hours = Math.Round(steamMinutes.Value / 60.0, 1);
        Started = entry.Started; Finished = entry.Finished;
        Review = entry.Review; Platform = entry.Platform;
        foreach (var f in FeatToggles) f.Selected = entry.Feats.Contains(f.Id);
        foreach (var c in Categories) c.Value = entry.Categories.GetValueOrDefault(c.Id);
        foreach (var m in Moods) m.Selected = entry.Moods.Contains(m.Id);
        Difficulty = entry.Difficulty ?? "";
        Runs.Clear(); foreach (var r in entry.Runs) Runs.Add(RunRow.From(r));
        NotifyCategories();
        foreach (var n in new[] { nameof(Name), nameof(Cover), nameof(Wide), nameof(IsNew), nameof(CanRemove), nameof(SteamMinutes), nameof(HasSteamHours), nameof(SteamHoursLabel), nameof(Status) }) OnPropertyChanged(n);
        opened = Snapshot();
    }

    public void SelectStatus(DiaryStatus s)
    {
        foreach (var o in Statuses) o.Selected = o.Status == s;
        if (s == DiaryStatus.Completed && Finished is null) Finished = DateTimeOffset.Now.Date;
        OnPropertyChanged(nameof(Status));
    }
    public void UseSteamHours() { if (SteamMinutes is { } m) Hours = Math.Round(m / 60.0, 1); }

    public DiaryProgressChange Save()
    {
        var e = entry.Clone();
        e.Status = Status; e.Rating = Rating <= 0 ? null : Rating; e.Minutes = (int)Math.Round(Math.Max(0, double.IsNaN(Hours) ? 0 : Hours) * 60);
        e.Started = Started?.Date; e.Finished = Status is DiaryStatus.Completed or DiaryStatus.Dropped ? Finished?.Date : null;
        e.Review = Review; e.Platform = Platform;
        e.Feats = [.. FeatToggles.Where(f => f.Selected).Select(f => f.Id)];
        e.Categories = Categories.Where(c => c.Value > 0).ToDictionary(c => c.Id, c => c.Value);
        e.Moods = [.. Moods.Where(m => m.Selected).Select(m => m.Id)];
        e.Difficulty = Difficulty.Length > 0 ? Difficulty : null;
        e.Runs = [.. Runs.Select(r => r.ToPlaythrough())];
        return diary.Save(e);
    }
    public void Remove() => diary.Remove(entry.Key);

    // Closing with changes asks first; incoherent dates are explained next to the fields (guide 14).
    private string opened = "";
    private string Snapshot() => $"{Status}|{Rating}|{Hours}|{Started?.Date}|{Finished?.Date}|{Review}|{Platform}|{string.Join(",", FeatToggles.Where(f => f.Selected).Select(f => f.Id))}|{string.Join(",", Categories.Select(c => c.Value))}|{string.Join(",", Moods.Where(m => m.Selected).Select(m => m.Id))}|{Difficulty}|{string.Join(";", Runs.Select(r => r.Key))}";
    public bool IsDirty => Snapshot() != opened;
    public string DateError => Started is { } s && Finished is { } f && f.Date < s.Date ? Text["DyDateOrder"] : "";
    public bool CanSave => DateError.Length == 0;
    partial void OnStartedChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(DateError)); OnPropertyChanged(nameof(CanSave)); }
    partial void OnFinishedChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(DateError)); OnPropertyChanged(nameof(CanSave)); }
}
