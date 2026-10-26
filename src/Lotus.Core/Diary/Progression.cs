namespace Lotus.Core.Diary;

// Gamification, computed from the diary: XP, level and title, badges and a few stats.
public sealed record Badge(string Id, string Glyph, bool Unlocked, int Current, int Goal)
{
    public double Fraction => Goal <= 0 ? 1 : Math.Clamp((double)Current / Goal, 0, 1);
}

public sealed record Progress(int Xp, int Level, int LevelStartXp, int NextLevelXp, string TitleKey, IReadOnlyList<Badge> Badges,
    int Completed, int Playing, int Want, int TotalMinutes, double? AverageRating, int CompletedThisYear, int Reviews)
{
    public double LevelFraction => NextLevelXp <= LevelStartXp ? 1 : Math.Clamp((double)(Xp - LevelStartXp) / (NextLevelXp - LevelStartXp), 0, 1);
    public int UnlockedBadges => Badges.Count(b => b.Unlocked);
}

public static class Progression
{
    // XP that one entry is worth. Finishing is the big one; thoughtful reviews and feats add on top.
    public static int Xp(DiaryEntry e)
    {
        var xp = e.Status switch { DiaryStatus.Completed => 100, DiaryStatus.Playing => 15, DiaryStatus.Paused => 10, DiaryStatus.Dropped => 10, _ => 5 };
        if (e.Rating is not null) xp += 10;
        xp += e.Review.Length >= 400 ? 60 : e.Review.Length >= 40 ? 30 : 0;
        foreach (var f in e.Feats) xp += f == "platinum" ? 60 : 20;
        xp += Math.Min(50, e.Minutes / 120); // 1 XP per 2 hours, up to 50
        return xp;
    }

    // Level n starts at 50·n·(n−1) XP: 0, 100, 300, 600, 1000, 1500…
    public static int LevelStart(int level) => 50 * level * (level - 1);
    public static int LevelFor(int xp) { var n = 1; while (LevelStart(n + 1) <= xp) n++; return n; }
    public static string TitleKey(int level) => level switch { 1 => "DyTitle1", <= 3 => "DyTitle2", <= 6 => "DyTitle3", <= 9 => "DyTitle4", <= 14 => "DyTitle5", <= 24 => "DyTitle6", <= 39 => "DyTitle7", _ => "DyTitle8" };

    // extraXp: XP earned outside the diary (Lotus achievements, Steam achievements).
    public static Progress Of(IReadOnlyList<DiaryEntry> entries, int extraXp = 0)
    {
        var xp = entries.Sum(Xp) + Math.Max(0, extraXp);
        var level = LevelFor(xp);
        var completed = entries.Where(e => e.Status == DiaryStatus.Completed).ToList();
        var reviews = entries.Count(e => e.Review.Length >= 40);
        var rated = entries.Where(e => e.Rating is not null).Select(e => e.Rating!.Value).ToList();
        int Feat(string id) => entries.Count(e => e.Feats.Contains(id));
        var genres = completed.SelectMany(e => e.Genres).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var badges = new List<Badge>
        {
            new("first", "", completed.Count >= 1, Math.Min(completed.Count, 1), 1),
            new("finisher5", "", completed.Count >= 5, Math.Min(completed.Count, 5), 5),
            new("finisher25", "", completed.Count >= 25, Math.Min(completed.Count, 25), 25),
            new("critic", "", reviews >= 5, Math.Min(reviews, 5), 5),
            new("essayist", "", entries.Any(e => e.Review.Length >= 1000), entries.Any(e => e.Review.Length >= 1000) ? 1 : 0, 1),
            new("masterpiece", "", rated.Any(r => r >= 10), rated.Any(r => r >= 10) ? 1 : 0, 1),
            new("platinum", "", Feat("platinum") >= 1, Math.Min(Feat("platinum"), 1), 1),
            new("perfectionist", "", Feat("platinum") >= 3, Math.Min(Feat("platinum"), 3), 3),
            new("hardcore", "", Feat("maxdiff") + Feat("nodeath") >= 1, Math.Min(Feat("maxdiff") + Feat("nodeath"), 1), 1),
            new("speedrunner", "", Feat("speedrun") >= 1, Math.Min(Feat("speedrun"), 1), 1),
            new("marathon", "", entries.Any(e => e.Minutes >= 100 * 60), entries.Any(e => e.Minutes >= 100 * 60) ? 1 : 0, 1),
            new("explorer", "", genres >= 5, Math.Min(genres, 5), 5),
            new("collector", "", entries.Count(e => e.Status == DiaryStatus.Want) >= 10, Math.Min(entries.Count(e => e.Status == DiaryStatus.Want), 10), 10),
            new("honest", "", entries.Any(e => e.Status == DiaryStatus.Dropped && e.Review.Length >= 40), entries.Any(e => e.Status == DiaryStatus.Dropped && e.Review.Length >= 40) ? 1 : 0, 1),
        };
        return new(xp, level, LevelStart(level), LevelStart(level + 1), TitleKey(level), badges,
            completed.Count, entries.Count(e => e.Status == DiaryStatus.Playing), entries.Count(e => e.Status == DiaryStatus.Want),
            entries.Sum(e => e.Minutes), rated.Count == 0 ? null : Math.Round(rated.Average(), 1),
            completed.Count(e => e.Finished?.Year == DateTime.Today.Year), reviews);
    }
}
