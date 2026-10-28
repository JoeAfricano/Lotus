using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Diary;

public sealed record DiaryTab(string Id, string Label, int Count);
public sealed record BadgeView(string Id, string Glyph, string Name, string Detail, bool Unlocked, string ProgressText, double Fraction);
public sealed record FeatChip(string Id, string Glyph, string Label);

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
        this.diary = diary; Text = text; Preferences = preferences;
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
    public string Section { get => section; set { if (value is "games" or "trophies" or "steam") SetProperty(ref section, value); } }
    public Progress Progress { get; private set; } = Progression.Of([]);
    public ObservableCollection<DiaryCard> Items { get; } = [];
    public ObservableCollection<DiaryTab> Tabs { get; } = [];
    public IReadOnlyList<BadgeView> Badges { get; private set; } = [];
    public IReadOnlyList<DiaryTab> SortOptions => [.. new[] { "recent", "rating", "hours", "name" }.Select(s => new DiaryTab(s, Text["DySort" + s], 0))];

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
        Fill();
        foreach (var p in typeof(DiaryViewModel).GetProperties().Where(p => p.Name is not (nameof(Items) or nameof(Tabs) or nameof(Text) or nameof(Preferences) or nameof(Diary) or nameof(Tab) or nameof(Query) or nameof(Sort) or nameof(Section) or nameof(Achievements))))
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
        rows = sort switch
        {
            "rating" => rows.OrderByDescending(e => e.Rating ?? -1).ThenBy(e => e.Name),
            "hours" => rows.OrderByDescending(e => e.Minutes),
            "name" => rows.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderByDescending(e => e.Updated),
        };
        Items.Clear();
        var i = 1; foreach (var e in rows) Items.Add(new(e, Text, i++));
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(NoMatch));
    }
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

// The "log this game" sheet: status, score, hours, dates, feats and review.
public sealed partial class DiaryEditorViewModel(GameDiary diary, ITextCatalog text) : ObservableObject
{
    private DiaryEntry entry = new();
    public ITextCatalog Text { get; } = text;
    public IReadOnlyList<StatusOption> Statuses { get; } = [.. new[] { (DiaryStatus.Completed, ""), (DiaryStatus.Playing, ""), (DiaryStatus.Dropped, ""), (DiaryStatus.Want, ""), (DiaryStatus.Paused, "") }
        .Select(s => new StatusOption(s.Item1, text["DyStatus" + s.Item1], s.Item2))];
    public IReadOnlyList<FeatToggle> FeatToggles { get; } = [.. Feats.All.Select(f => new FeatToggle(f.Id, f.Glyph, text["DyFeat" + f.Id]))];
    public IReadOnlyList<string> Platforms { get; } = ["PC", "Steam Deck", "Console", "Mobile"];

    public string Name => entry.Name;
    public string? Cover => entry.Cover;
    public string? Wide => entry.Wide;
    public bool IsNew { get; private set; }
    public bool CanRemove => !IsNew;
    public int? SteamMinutes { get; private set; }
    public bool HasSteamHours => SteamMinutes is > 0;
    public string SteamHoursLabel => SteamMinutes is { } m ? Text.Format("DyUseSteamHours", DiaryText.Hours(m)) : "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RatingText), nameof(RatingKind))] private double rating;
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
        return diary.Save(e);
    }
    public void Remove() => diary.Remove(entry.Key);

    // Closing with changes asks first; incoherent dates are explained next to the fields (guide 14).
    private string opened = "";
    private string Snapshot() => $"{Status}|{Rating}|{Hours}|{Started?.Date}|{Finished?.Date}|{Review}|{Platform}|{string.Join(",", FeatToggles.Where(f => f.Selected).Select(f => f.Id))}";
    public bool IsDirty => Snapshot() != opened;
    public string DateError => Started is { } s && Finished is { } f && f.Date < s.Date ? Text["DyDateOrder"] : "";
    public bool CanSave => DateError.Length == 0;
    partial void OnStartedChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(DateError)); OnPropertyChanged(nameof(CanSave)); }
    partial void OnFinishedChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(DateError)); OnPropertyChanged(nameof(CanSave)); }
}
