namespace Lotus.Core.Diary;

// The player's game diary (in the spirit of MyGameList / Backloggd): every game they want, play, finish or drop,
// with their own score, hours, dates, feats and review. Stored only on this PC. Progress (XP, levels, badges) is
// always computed from the entries, so it can never drift from what the player wrote.
public enum DiaryStatus { Want, Playing, Paused, Completed, Dropped }

public sealed class DiaryEntry
{
    public string Key { get; set; } = "";          // "steam:<appid>" when known, otherwise the installed game's key
    public string Name { get; set; } = "";
    public string? AppId { get; set; }
    public string? Cover { get; set; }             // portrait art (URL or local file)
    public string? Wide { get; set; }              // wide art for headers
    public List<string> Genres { get; set; } = [];
    public DiaryStatus Status { get; set; } = DiaryStatus.Want;
    public double? Rating { get; set; }            // 0.5–10 in half steps; null = not rated
    public int Minutes { get; set; }
    public DateTime? Started { get; set; }
    public DateTime? Finished { get; set; }
    public List<string> Feats { get; set; } = [];
    public string Review { get; set; } = "";
    public string Platform { get; set; } = "PC";
    // Optional details: scores per category (story, gameplay, visuals, sound), how it felt, the difficulty played and
    // extra playthroughs (the main dates and hours stay on the entry itself).
    public Dictionary<string, double> Categories { get; set; } = [];
    public List<string> Moods { get; set; } = [];
    public string? Difficulty { get; set; }
    public List<Playthrough> Runs { get; set; } = [];
    // Sessions recorded by Lotus while the game was open (when, how long); the minutes are already in Minutes.
    public List<PlaySession> Sessions { get; set; } = [];
    public DateTimeOffset Added { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;

    public DiaryEntry Clone()
    {
        var c = (DiaryEntry)MemberwiseClone();
        c.Genres = [.. Genres]; c.Feats = [.. Feats]; c.Moods = [.. Moods]; c.Categories = new(Categories); // lists are not shared with the copy
        c.Runs = [.. Runs.Select(r => r with { })];
        c.Sessions = [.. Sessions];
        return c;
    }
    public static string SteamKey(string appId) => "steam:" + appId;
}

// One play session Lotus saw (game open from Start, for Minutes).
public sealed record PlaySession(DateTime Start, int Minutes);

// One more time through a game (Backloggd-style): its own dates, hours and platform.
public sealed record Playthrough(DateTime? Started, DateTime? Finished, int Minutes, string Platform, string Note = "");

public static class DiaryOptions
{
    public static readonly string[] Categories = ["story", "gameplay", "visuals", "sound"];
    public static readonly (string Id, string Glyph)[] Moods = [("exciting", "E945"), ("relaxing", "E706"), ("challenging", "E7BA"), ("addictive", "E7FC"), ("emotional", "EB51"), ("scary", "E7C3"), ("beautiful", "E790"), ("funny", "E76E")];
    public static readonly string[] Difficulties = ["easy", "normal", "hard", "hardest"];
}

public sealed class DiaryData
{
    public List<DiaryEntry> Entries { get; set; } = [];
    public string Bio { get; set; } = "";
    public List<DiaryFilter> Filters { get; set; } = []; // filters the player saved with a name
    public List<string> Showcase { get; set; } = []; // "Top 4" favorites: up to 4 entry keys, in order (empty string = free slot)
}

// A saved combination of diary filters (any field left null means "any").
public sealed class DiaryFilter
{
    public string Name { get; set; } = "";
    public string Tab { get; set; } = "all";
    public string? Genre { get; set; }
    public string? Platform { get; set; }
    public string? Store { get; set; }
    public string? Rating { get; set; }
    public string? Year { get; set; }
    public string Sort { get; set; } = "recent";
}

public interface IGameDiaryStore { DiaryData Load(); void Save(DiaryData data); }

// What a save changed in the player's progress: drives the celebration screen.
public sealed record DiaryProgressChange(int XpBefore, int XpAfter, int LevelBefore, int LevelAfter, IReadOnlyList<Badge> NewBadges, bool JustCompleted, DiaryEntry Entry)
{
    public int XpGained => XpAfter - XpBefore;
    public bool LevelUp => LevelAfter > LevelBefore;
    public bool WorthCelebrating => JustCompleted || LevelUp || NewBadges.Count > 0;
}

public sealed class GameDiary(IGameDiaryStore store, Action<Exception> log)
{
    public const int ReviewLimit = 5000, BioLimit = 160;
    private readonly DiaryData data = Load(store, log);
    public event EventHandler? Changed;
    // Raised when XP from outside the diary (achievements, Steam) changes.
    public event EventHandler? ProgressChanged;
    public Func<int>? ExtraXp { get; set; }
    public void NotifyProgress() => ProgressChanged?.Invoke(this, EventArgs.Empty);

    public IReadOnlyList<DiaryEntry> Entries => data.Entries;
    public string Bio => data.Bio;
    public DiaryEntry? Get(string key) => data.Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
    public bool Contains(string key) => Get(key) is not null;
    public Progress Progress => Progression.Of(data.Entries, ExtraXp?.Invoke() ?? 0);

    // Saves (adds or replaces) an entry, cleaning the input, and reports what it did to the player's progress.
    public DiaryProgressChange Save(DiaryEntry input)
    {
        var before = Progress; var badgesBefore = before.Badges.Where(b => b.Unlocked).Select(b => b.Id).ToHashSet();
        var entry = Clean(input.Clone());
        var old = Get(entry.Key);
        var justCompleted = entry.Status == DiaryStatus.Completed && old?.Status != DiaryStatus.Completed;
        if (justCompleted && entry.Finished is null) entry.Finished = DateTime.Today;
        if (entry.Status is DiaryStatus.Playing or DiaryStatus.Completed && entry.Started is null) entry.Started = old?.Started ?? (entry.Status == DiaryStatus.Playing ? DateTime.Today : null);
        entry.Added = old?.Added ?? DateTimeOffset.Now; entry.Updated = DateTimeOffset.Now;
        if (old is not null) data.Entries[data.Entries.IndexOf(old)] = entry; else data.Entries.Add(entry);
        Persist();
        var after = Progress;
        var newBadges = after.Badges.Where(b => b.Unlocked && !badgesBefore.Contains(b.Id)).ToList();
        return new(before.Xp, after.Xp, before.Level, after.Level, newBadges, justCompleted, entry);
    }

    // "Quero jogar" from the Game list: adds the game as Want (or leaves an existing entry alone).
    public DiaryProgressChange? Want(DiaryEntry game) => Contains(game.Key) ? null : Save(Seed(game, DiaryStatus.Want));

    // A session Lotus saw: adds the time, starts the game in the diary if needed and moves "Want" / "Paused" to
    // "Playing" (finished or dropped games keep their status). Null when the session is too short to count.
    public const int SessionLimit = 400;
    public DiaryProgressChange? RecordSession(DiaryEntry game, DateTime start, int minutes)
    {
        if (minutes < 1) return null;
        var e = (Get(game.Key) ?? Seed(game, DiaryStatus.Playing)).Clone();
        e.Minutes += minutes;
        e.Sessions.Add(new(start, minutes));
        if (e.Sessions.Count > SessionLimit) e.Sessions.RemoveRange(0, e.Sessions.Count - SessionLimit);
        if (e.Status is DiaryStatus.Want or DiaryStatus.Paused) e.Status = DiaryStatus.Playing;
        e.Started ??= start.Date;
        e.Cover ??= game.Cover; e.Wide ??= game.Wide; e.AppId ??= game.AppId;
        return Save(e);
    }

    public void Remove(string key)
    {
        if (Get(key) is { } e) { data.Entries.Remove(e); Persist(); }
    }

    // Saved filters: a new one with the same name replaces the old one.
    public IReadOnlyList<DiaryFilter> Filters => data.Filters;
    public void SaveFilter(DiaryFilter filter)
    {
        var name = (filter.Name ?? "").Trim();
        if (name.Length == 0) return;
        filter.Name = name.Length > 40 ? name[..40] : name;
        data.Filters.RemoveAll(f => string.Equals(f.Name, filter.Name, StringComparison.CurrentCultureIgnoreCase));
        data.Filters.Add(filter); Persist();
    }
    public void DeleteFilter(string name) { if (data.Filters.RemoveAll(f => f.Name == name) > 0) Persist(); }

    // "Top 4" showcase: slot 0-3 holds an entry key; null clears the slot. A game appears in one slot only.
    public const int ShowcaseSize = 4;
    public IReadOnlyList<string> Showcase => data.Showcase;
    public void SetShowcase(int slot, string? key)
    {
        if (slot is < 0 or >= ShowcaseSize) return;
        while (data.Showcase.Count < ShowcaseSize) data.Showcase.Add("");
        if (key is not null) for (var i = 0; i < ShowcaseSize; i++) if (data.Showcase[i] == key) data.Showcase[i] = "";
        data.Showcase[slot] = key ?? "";
        Persist();
    }

    public void SetBio(string? bio)
    {
        var clean = (bio ?? "").Trim().ReplaceLineEndings(" ");
        if (clean.Length > BioLimit) clean = clean[..BioLimit];
        if (clean == data.Bio) return;
        data.Bio = clean; Persist();
    }

    public static DiaryEntry Seed(DiaryEntry game, DiaryStatus status)
    {
        var e = game.Clone(); e.Status = status; return e;
    }

    private static DiaryEntry Clean(DiaryEntry e)
    {
        e.Name = e.Name.Trim();
        e.Review = (e.Review ?? "").Trim();
        if (e.Review.Length > ReviewLimit) e.Review = e.Review[..ReviewLimit];
        if (e.Rating is { } r) e.Rating = r <= 0 ? null : Math.Clamp(Math.Round(r * 2) / 2, 0.5, 10);
        e.Minutes = Math.Clamp(e.Minutes, 0, 100_000 * 60);
        e.Feats = [.. e.Feats.Where(Feats.IsKnown).Distinct()];
        if (e.Started is { } s && e.Finished is { } f && f < s) e.Finished = s;
        if (string.IsNullOrWhiteSpace(e.Platform)) e.Platform = "PC";
        return e;
    }

    private void Persist()
    {
        try { store.Save(data); } catch (Exception ex) { log(ex); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private static DiaryData Load(IGameDiaryStore store, Action<Exception> log)
    {
        try { return store.Load(); } catch (Exception ex) { log(ex); return new(); }
    }
}

// Things a player can proudly mark on a game.
public static class Feats
{
    public static readonly (string Id, string Glyph)[] All =
    [
        ("platinum", ""), ("ngplus", ""), ("maxdiff", ""), ("nodeath", ""),
        ("speedrun", ""), ("pacifist", ""), ("allendings", ""), ("coop", ""),
    ];
    public static bool IsKnown(string id) => All.Any(f => f.Id == id);
}
