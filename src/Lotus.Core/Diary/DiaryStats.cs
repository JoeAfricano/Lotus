using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Diary;

// Numbers behind the diary's "Statistics" tab and the yearly recap. Pure math over the entries (testable).
public sealed record DiaryStatsData(
    int Games, int Completed, int TotalMinutes, double? AverageRating, double? AverageDaysToFinish, int? AverageMinutesToFinish,
    int[] MonthMinutes, int[] RatingBuckets,
    IReadOnlyList<(string Name, int Count)> Genres, IReadOnlyList<(string Name, int Count)> Platforms,
    IReadOnlyList<(string Id, int Count)> Moods, IReadOnlyList<(string Id, double Average)> Categories, IReadOnlyList<(string Id, int Count)> Difficulties,
    // Year recap
    int Year, int YearMinutes, int YearPlayed, int YearCompleted, int PeakMonth, string? TopGenre, DiaryEntry? BestGame, DiaryEntry? MostPlayed, string? TopMood, IReadOnlyList<int> Years);

public static class DiaryStats
{
    // Hours have no dates of their own, so each game's time is spread evenly over the months between its start and
    // its end (finish date, or today while still playing). Extra playthroughs count with their own dates.
    public static DiaryStatsData Compute(IReadOnlyList<DiaryEntry> entries, int year, DateTime today)
    {
        var months = new int[12];
        var perGameInYear = new Dictionary<DiaryEntry, int>();
        void Spread(DiaryEntry e, int minutes, DateTime? started, DateTime? finished, bool open)
        {
            if (minutes <= 0) return;
            var end = (finished ?? (open ? today : e.Updated.LocalDateTime)).Date;
            var start = (started ?? finished ?? end).Date;
            if (end < start) (start, end) = (end, start);
            var first = new DateTime(start.Year, start.Month, 1); var last = new DateTime(end.Year, end.Month, 1);
            var count = (last.Year - first.Year) * 12 + last.Month - first.Month + 1;
            var share = minutes / (double)count;
            for (var m = first; m <= last; m = m.AddMonths(1))
            {
                if (m.Year != year) continue;
                var part = (int)Math.Round(share);
                months[m.Month - 1] += part;
                perGameInYear[e] = perGameInYear.GetValueOrDefault(e) + part;
            }
        }
        foreach (var e in entries)
        {
            Spread(e, e.Minutes, e.Started, e.Finished, e.Status == DiaryStatus.Playing);
            foreach (var r in e.Runs) Spread(e, r.Minutes, r.Started, r.Finished, false);
        }

        var buckets = new int[10];
        foreach (var r in entries.Select(e => e.Rating).OfType<double>()) buckets[Math.Clamp((int)Math.Ceiling(r), 1, 10) - 1]++;

        var completed = entries.Where(e => e.Status == DiaryStatus.Completed).ToList();
        var days = completed.Where(e => e is { Started: not null, Finished: not null } && e.Finished >= e.Started).Select(e => (e.Finished!.Value - e.Started!.Value).TotalDays).ToList();
        var toFinish = completed.Where(e => e.Minutes > 0).Select(e => e.Minutes).ToList();
        var rated = entries.Select(e => e.Rating).OfType<double>().ToList();

        static IReadOnlyList<(string, int)> Top(IEnumerable<string> values, int max) =>
            [.. values.Where(v => v.Length > 0).GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCulture).Take(max).Select(g => (g.Key, g.Count()))];
        var categories = DiaryOptions.Categories.Select(c => (c, entries.Where(e => e.Categories.ContainsKey(c)).Select(e => e.Categories[c]).ToList()))
            .Where(x => x.Item2.Count > 0).Select(x => (x.c, Math.Round(x.Item2.Average(), 1))).ToList();
        var difficulties = DiaryOptions.Difficulties.Select(d => (d, entries.Count(e => e.Difficulty == d))).Where(x => x.Item2 > 0).ToList();

        // Year recap: games touched in the year (time spread into it, or started/finished in it).
        bool InYear(DateTime? d) => d?.Year == year;
        var played = entries.Where(e => perGameInYear.ContainsKey(e) || InYear(e.Started) || InYear(e.Finished) || e.Runs.Any(r => InYear(r.Started) || InYear(r.Finished))).ToList();
        var finishedInYear = entries.Where(e => InYear(e.Finished) || e.Runs.Any(r => InYear(r.Finished))).ToList();
        var peak = months.Max() > 0 ? Array.IndexOf(months, months.Max()) + 1 : 0;
        var years = entries.SelectMany(e => new[] { e.Started, e.Finished }.Concat(e.Runs.SelectMany(r => new[] { r.Started, r.Finished }))).OfType<DateTime>().Select(d => d.Year)
            .Append(today.Year).Distinct().OrderByDescending(y => y).ToList();

        return new(entries.Count, completed.Count, entries.Sum(e => e.Minutes + e.Runs.Sum(r => r.Minutes)),
            rated.Count > 0 ? Math.Round(rated.Average(), 1) : null, days.Count > 0 ? days.Average() : null, toFinish.Count > 0 ? (int)toFinish.Average() : null,
            months, buckets,
            Top(entries.SelectMany(e => e.Genres.Take(3)), 6), Top(entries.Select(e => e.Platform), 6),
            Top(entries.SelectMany(e => e.Moods), 8), categories, difficulties,
            year, months.Sum(), played.Count, finishedInYear.Count, peak,
            Top(played.SelectMany(e => e.Genres.Take(2)), 1).Select(g => g.Item1).FirstOrDefault(),
            finishedInYear.Concat(played).Where(e => e.Rating is not null).OrderByDescending(e => e.Rating).ThenByDescending(e => e.Minutes).FirstOrDefault(),
            perGameInYear.Count > 0 ? perGameInYear.OrderByDescending(p => p.Value).First().Key : null,
            Top(played.SelectMany(e => e.Moods), 1).Select(m => m.Item1).FirstOrDefault(),
            years);
    }
}

// One bar: label, value text (always shown as text, not only as length), length 0–1 and a hover hint.
public sealed record StatBar(string Label, string ValueText, double Fraction, string Hint, bool Highlight = false, bool ShowValue = true)
{
    public double Scaled(double max) => Math.Max(Fraction * max, Fraction > 0 ? 3 : 0);
}
public sealed record StatTile(string Glyph, string Value, string Label);
public sealed record StatChip(string Glyph, string Label, string Count);

public sealed class DiaryStatsViewModel(GameDiary diary, ITextCatalog text) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    private int year = DateTime.Today.Year;
    public DiaryStatsData Data { get; private set; } = DiaryStats.Compute([], DateTime.Today.Year, DateTime.Today);
    public int Year { get => year; set { if (SetProperty(ref year, value)) Refresh(); } }
    public IReadOnlyList<int> Years => Data.Years;
    public bool IsEmpty => Data.Games == 0;
    public bool HasData => !IsEmpty;
    public IReadOnlyList<StatTile> Tiles { get; private set; } = [];
    public IReadOnlyList<StatBar> MonthBars { get; private set; } = [];
    public IReadOnlyList<StatBar> RatingBars { get; private set; } = [];
    public IReadOnlyList<StatBar> GenreBars { get; private set; } = [];
    public IReadOnlyList<StatBar> PlatformBars { get; private set; } = [];
    public IReadOnlyList<StatBar> CategoryBars { get; private set; } = [];
    public IReadOnlyList<StatChip> MoodChips { get; private set; } = [];
    public IReadOnlyList<StatChip> DifficultyChips { get; private set; } = [];
    public bool HasCategories => CategoryBars.Count > 0;
    public bool HasMoods => MoodChips.Count > 0 || DifficultyChips.Count > 0;
    public bool HasYearHours => Data.YearMinutes > 0;
    public string RecapText => Text.Format("DyStRecap", year);
    public bool HasGenres => GenreBars.Count > 0;
    public string MonthTitle => Text.Format("DyStMonthTitle", year);
    public string MonthSummary => Data.YearMinutes > 0 ? Text.Format("DyStMonthSummary", DiaryText.Hours(Data.YearMinutes), MonthName(Data.PeakMonth, full: true)) : Text["DyStMonthEmpty"];
    public string RatingSummary => Data.AverageRating is { } a ? Text.Format("DyStRatingSummary", a.ToString("0.0", Culture)) : Text["DyStRatingEmpty"];
    private CultureInfo Culture => CultureInfo.GetCultureInfo(Text.Language);
    private string MonthName(int month, bool full = false) => month is < 1 or > 12 ? "—" : (full ? Culture.DateTimeFormat.MonthNames[month - 1] : Culture.DateTimeFormat.AbbreviatedMonthNames[month - 1].TrimEnd('.'));

    public void Refresh()
    {
        Data = DiaryStats.Compute(diary.Entries, year, DateTime.Today);
        var d = Data;
        Tiles =
        [
            new("", d.Games.ToString(CultureInfo.InvariantCulture), Text["DyStGames"]),
            new("", d.Completed.ToString(CultureInfo.InvariantCulture), Text["DyStCompleted"]),
            new("", DiaryText.Hours(d.TotalMinutes), Text["DyStHours"]),
            new("", d.AverageRating?.ToString("0.0", Culture) ?? "—", Text["DyStAverage"]),
            new("", d.AverageDaysToFinish is { } days ? (days < 1 ? Text["DyStDaysLess"] : Text.Format(Math.Round(days) == 1 ? "DyStDay" : "DyStDays", Math.Round(days))) : "—", Text["DyStDaysToFinish"]),
            new("", d.AverageMinutesToFinish is { } m ? DiaryText.Hours(m) : "—", Text["DyStHoursToFinish"]),
        ];
        var maxMonth = Math.Max(1, d.MonthMinutes.Max());
        MonthBars = [.. d.MonthMinutes.Select((m, i) => new StatBar(MonthName(i + 1), m > 0 ? DiaryText.Hours(m) : "", m / (double)maxMonth,
            $"{MonthName(i + 1, full: true)} {year}: {(m > 0 ? DiaryText.Hours(m) : "0h")}", Highlight: i + 1 == d.PeakMonth, ShowValue: i + 1 == d.PeakMonth))];
        var maxRating = Math.Max(1, d.RatingBuckets.Max());
        RatingBars = [.. d.RatingBuckets.Select((c, i) => new StatBar((i + 1).ToString(CultureInfo.InvariantCulture), c > 0 ? c.ToString(CultureInfo.InvariantCulture) : "",
            c / (double)maxRating, Text.Format("DyStRatingHint", c, i == 0 ? "0.5–1" : $"{i}.5–{i + 1}"), Highlight: c == maxRating && c > 0))];
        StatBar Count((string Name, int Count) x, int max, string label) => new(label, x.Count.ToString(CultureInfo.InvariantCulture), x.Count / (double)max, Text.Format("DyStGamesHint", label, x.Count));
        var g = Math.Max(1, d.Genres.Select(x => x.Count).DefaultIfEmpty(1).Max());
        GenreBars = [.. d.Genres.Select(x => Count(x, g, x.Name))];
        var p = Math.Max(1, d.Platforms.Select(x => x.Count).DefaultIfEmpty(1).Max());
        PlatformBars = [.. d.Platforms.Select(x => Count(x, p, x.Name))];
        CategoryBars = [.. d.Categories.Select(c => new StatBar(Text["DyCat" + c.Id], c.Average.ToString("0.0", Culture), c.Average / 10, Text["DyCat" + c.Id] + ": " + c.Average.ToString("0.0", Culture)))];
        MoodChips = [.. d.Moods.Select(m => new StatChip(char.ConvertFromUtf32(Convert.ToInt32(DiaryOptions.Moods.First(x => x.Id == m.Id).Glyph, 16)), Text["DyMood" + m.Id], m.Count.ToString(CultureInfo.InvariantCulture)))];
        DifficultyChips = [.. d.Difficulties.Select(x => new StatChip("", Text["DyDiff" + x.Id], x.Count.ToString(CultureInfo.InvariantCulture)))];
        foreach (var n in new[] { nameof(Data), nameof(Years), nameof(IsEmpty), nameof(HasData), nameof(Tiles), nameof(MonthBars), nameof(RatingBars), nameof(GenreBars), nameof(PlatformBars), nameof(CategoryBars),
            nameof(MoodChips), nameof(DifficultyChips), nameof(HasCategories), nameof(HasMoods), nameof(HasYearHours), nameof(MonthTitle), nameof(RecapText), nameof(HasGenres), nameof(MonthSummary), nameof(RatingSummary) })
            OnPropertyChanged(n);
    }
}

// One screen of the yearly recap. CountTo makes the big number count up on screen; Art shows a game's art.
public sealed record RecapSlide(string Id, string Glyph, string Eyebrow, string Big, string Title, string Detail, string? Art = null, int? CountTo = null, string Suffix = "", string? ArtFallback = null);
public sealed record RecapLine(string Glyph, string Label, string Value);

// Yearly recap ("Wrapped" style): a few screens from the year's numbers, ending on a summary card that can be saved.
public sealed class DiaryRecapViewModel(GameDiary diary, ITextCatalog text)
{
    public ITextCatalog Text { get; } = text;
    public int Year { get; private set; }
    public string PlayerName { get; private set; } = "";
    public IReadOnlyList<RecapSlide> Slides { get; private set; } = [];
    public IReadOnlyList<RecapLine> Summary { get; private set; } = [];
    public DiaryStatsData Data { get; private set; } = DiaryStats.Compute([], DateTime.Today.Year, DateTime.Today);
    public string SummaryTitle => Text.Format("DyRcSummaryTitle", Year);
    public string? SummaryArt => (Data.BestGame ?? Data.MostPlayed) is { } g ? g.Cover ?? Banner(g) : null;
    public string? SummaryArtFallback => (Data.BestGame ?? Data.MostPlayed) is { } g ? Banner(g) : null;
    // The card is portrait, so the portrait cover comes first. // Steam games: the wide "hero" art (no logo cropped in); otherwise the art the diary already has.
    public static string? Banner(DiaryEntry e) => e.AppId is { Length: > 0 } id ? Catalog.CatalogText.Hero(id) : e.Wide ?? e.Cover;

    public void Open(int year, string playerName, DateTime? today = null)
    {
        Year = year; PlayerName = playerName;
        var d = Data = DiaryStats.Compute(diary.Entries, year, today ?? DateTime.Today);
        var culture = CultureInfo.GetCultureInfo(Text.Language);
        string Month(int m) => m is >= 1 and <= 12 ? culture.DateTimeFormat.MonthNames[m - 1] : "—";
        string Rating(double r) => r.ToString(r % 1 == 0 ? "0" : "0.0", culture);
        var eyebrow = Text.Format("DyRcEyebrow", year).ToUpperInvariant();
        var slides = new List<RecapSlide> { new("intro", "", eyebrow, year.ToString(CultureInfo.InvariantCulture), Text.Format("DyRcIntroTitle", playerName), Text["DyRcIntroDetail"]) };
        if (d.YearPlayed == 0)
            slides.Add(new("empty", "", eyebrow, "0", Text.Format("DyRcEmptyTitle", year), Text["DyRcEmptyDetail"]));
        else
        {
            if (d.YearMinutes > 0)
                slides.Add(new("hours", "", eyebrow, DiaryText.Hours(d.YearMinutes), Text["DyRcHoursTitle"], Text.Format("DyRcHoursDetail", Month(d.PeakMonth)),
                    CountTo: d.YearMinutes >= 60 ? (int)Math.Round(d.YearMinutes / 60.0) : null, Suffix: "h"));
            slides.Add(new("games", "", eyebrow, d.YearPlayed.ToString(CultureInfo.InvariantCulture), Text[d.YearPlayed == 1 ? "DyRcGamesTitleOne" : "DyRcGamesTitle"],
                Text.Format("DyRcGamesDetail", d.YearCompleted), CountTo: d.YearPlayed));
            if (d.TopGenre is { } g) slides.Add(new("genre", "", eyebrow, g, Text["DyRcGenreTitle"], Text["DyRcGenreDetail"]));
            if (d.MostPlayed is { } mp) slides.Add(new("most", "", eyebrow, mp.Name, Text["DyRcMostTitle"], Text["DyRcMostDetail"], mp.Cover ?? Banner(mp), ArtFallback: Banner(mp)));
            if (d.BestGame is { Rating: { } br } best) slides.Add(new("best", "", eyebrow, Rating(br), Text.Format("DyRcBestTitle", best.Name), Text[br >= 10 ? "DyRcBestPerfect" : "DyRcBestDetail"], best.Cover ?? Banner(best), ArtFallback: Banner(best)));
            if (d.TopMood is { } mood) slides.Add(new("mood", char.ConvertFromUtf32(Convert.ToInt32(DiaryOptions.Moods.First(m => m.Id == mood).Glyph, 16)), eyebrow, Text["DyMood" + mood], Text["DyRcMoodTitle"], Text["DyRcMoodDetail"]));
        }
        slides.Add(new("summary", "", eyebrow, year.ToString(CultureInfo.InvariantCulture), Text.Format("DyRcSummaryTitle", year), Text["DyRcSummaryDetail"]));
        Slides = slides;
        Summary =
        [
            new("", Text["DyRcLineHours"], d.YearMinutes > 0 ? DiaryText.Hours(d.YearMinutes) : "—"),
            new("", Text["DyRcLinePlayed"], d.YearPlayed.ToString(CultureInfo.InvariantCulture)),
            new("", Text["DyRcLineCompleted"], d.YearCompleted.ToString(CultureInfo.InvariantCulture)),
            new("", Text["DyRcLineGenre"], d.TopGenre ?? "—"),
            new("", Text["DyRcLineBest"], d.BestGame is { Rating: { } r } b ? $"{b.Name} · {Rating(r)}" : "—"),
            new("", Text["DyRcLinePeak"], d.PeakMonth > 0 ? Month(d.PeakMonth) : "—"),
        ];
    }
}
