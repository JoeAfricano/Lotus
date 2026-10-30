using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Diary;

// "O que jogar agora?": picks from the player's own list (Playing, Paused, Want) by the time they have, the vibe they
// want and what they usually rate well. Every pick says why, in plain words.
public enum PlayTime { Short, Evening, Long }
public sealed record PlayReason(string Key, string Arg = "");
public sealed record PlayPick(DiaryEntry Entry, double Score, IReadOnlyList<PlayReason> Reasons);

public static class PlayNext
{
    public static readonly string[] Vibes = ["any", "relax", "challenge", "story", "action"];
    // Genre words (Steam genres come in the player's language) and the diary moods that fit each vibe.
    private static readonly Dictionary<string, (string[] Genres, string Mood)> VibeMap = new()
    {
        ["relax"] = (["casual", "simula", "puzzle", "quebra", "fazenda", "farming", "cozy", "aconchegante"], "relaxing"),
        ["challenge"] = (["rogue", "souls", "estratég", "strategy", "plataforma", "platform", "luta", "fighting", "difícil"], "challenging"),
        ["story"] = (["rpg", "aventura", "adventure", "narrativ", "story", "história", "visual novel"], "emotional"),
        ["action"] = (["ação", "action", "tiro", "shooter", "fps", "corrida", "racing", "esporte", "sport", "hack"], "exciting"),
    };

    public static bool FitsVibe(DiaryEntry e, string vibe) =>
        vibe == "any" || (VibeMap.TryGetValue(vibe, out var v) && (e.Moods.Contains(v.Mood) || e.Genres.Any(g => v.Genres.Any(w => g.Contains(w, StringComparison.OrdinalIgnoreCase)))));

    public static IReadOnlyList<PlayPick> Suggest(IReadOnlyList<DiaryEntry> entries, PlayTime time, string vibe, DateTime today, int shuffle = 0, int take = 3)
    {
        // What the player rates well: average rating per genre (only genres with rated games).
        var affinity = entries.Where(e => e.Rating is not null).SelectMany(e => e.Genres.Take(3).Select(g => (g, r: e.Rating!.Value)))
            .GroupBy(x => x.g, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (Avg: g.Average(x => x.r), Count: g.Count()), StringComparer.OrdinalIgnoreCase);
        var picks = new List<PlayPick>();
        foreach (var e in entries.Where(e => e.Status is DiaryStatus.Playing or DiaryStatus.Paused or DiaryStatus.Want))
        {
            var reasons = new List<PlayReason>();
            double score = e.Status switch { DiaryStatus.Playing => 3, DiaryStatus.Paused => 2, _ => 1.5 };
            var lastPlayed = e.Sessions.Count > 0 ? e.Sessions[^1].Start : (DateTime?)null;
            if (e.Status == DiaryStatus.Playing)
            {
                reasons.Add(new("PnPlaying"));
                if (lastPlayed is { } lp && (today - lp.Date).TotalDays <= 7) { score += 1; reasons.Add(new("PnMomentum")); }
            }
            // Time: little time favours what is already going; a long session is a good moment to start something.
            score += (time, e.Status) switch
            {
                (PlayTime.Short, DiaryStatus.Playing) => 2, (PlayTime.Short, DiaryStatus.Paused) => 0.5, (PlayTime.Short, _) => -0.5,
                (PlayTime.Long, DiaryStatus.Want) => 1.5, (PlayTime.Long, DiaryStatus.Paused) => 1,
                _ => 0,
            };
            if (time == PlayTime.Long && e.Status == DiaryStatus.Want) reasons.Add(new("PnStartNew"));
            if (e.Status == DiaryStatus.Paused) reasons.Add(new("PnPaused", (lastPlayed ?? e.Updated.LocalDateTime).ToString("d", CultureInfo.CurrentCulture)));
            if (e.Status == DiaryStatus.Want && (today - e.Added.LocalDateTime.Date).TotalDays is >= 30 and var days) { score += Math.Min(1, days / 365.0); reasons.Add(new("PnWaiting", ((int)days).ToString(CultureInfo.InvariantCulture))); }
            // Taste: the best-rated genre of this game among the player's own scores.
            var best = e.Genres.Take(3).Select(g => affinity.TryGetValue(g, out var a) ? (g, a.Avg, a.Count) : default).Where(x => x.g is not null).OrderByDescending(x => x.Avg).FirstOrDefault();
            if (best.g is not null) { score += (best.Avg - 6) / 2; if (best.Avg >= 7.5) reasons.Add(new("PnTaste", best.g)); }
            if (vibe != "any") { if (FitsVibe(e, vibe)) { score += 2.5; reasons.Add(new("PnVibe" + vibe)); } else score -= 1.5; }
            // "Outra sugestão": a small stable shuffle so the same list does not always win.
            score += Jitter(e.Key, shuffle);
            picks.Add(new(e, score, reasons));
        }
        return [.. picks.OrderByDescending(p => p.Score).ThenBy(p => p.Entry.Name, StringComparer.CurrentCulture).Take(take)];
    }

    private static double Jitter(string key, int shuffle)
    {
        if (shuffle == 0) return 0;
        var h = 17; foreach (var c in key) h = unchecked(h * 31 + c); h = unchecked(h * 397 ^ shuffle * 7919);
        return (h & 0xFFFF) / 65535.0 * 2.5;
    }
}

public sealed class PlayNextViewModel(GameDiary diary, ITextCatalog text) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    private PlayTime time = PlayTime.Evening; private string vibe = "any"; private int shuffle;
    public PlayTime Time { get => time; set { if (SetProperty(ref time, value)) { shuffle = 0; Refresh(keep: false); } } }
    public string Vibe { get => vibe; set { if (value is not null && SetProperty(ref vibe, value)) { shuffle = 0; Refresh(keep: false); } } }
    public IReadOnlyList<PlayPick> Picks { get; private set; } = [];
    public PlayPick? Top => Picks.Count > 0 ? Picks[0] : null;
    public bool HasPick => Top is not null;
    public bool IsEmpty => !HasPick;
    public string TopReasons => Top is null ? "" : string.Join(" · ", Top.Reasons.Take(3).Select(Reason));
    public IReadOnlyList<PlayPick> Others => [.. Picks.Skip(1)];
    public string Reason(PlayReason r) => r.Arg.Length > 0 ? Text.Format(r.Key, r.Arg) : Text[r.Key];
    // "Outra sugestão": the games already shown step aside until the list runs out.
    private readonly HashSet<string> shown = new(StringComparer.OrdinalIgnoreCase);
    public void Another() { if (Top is { } t) shown.Add(t.Entry.Key); shuffle++; Refresh(keep: true); }
    public void Refresh() => Refresh(keep: false);
    private void Refresh(bool keep)
    {
        if (!keep) shown.Clear();
        var all = PlayNext.Suggest(diary.Entries, time, vibe, DateTime.Today, shuffle, take: int.MaxValue);
        var fresh = all.Where(p => !shown.Contains(p.Entry.Key)).ToList();
        if (fresh.Count == 0) { shown.Clear(); fresh = [.. all]; }
        Picks = [.. fresh.Take(3)];
        foreach (var n in new[] { nameof(Picks), nameof(Top), nameof(HasPick), nameof(IsEmpty), nameof(TopReasons), nameof(Others) }) OnPropertyChanged(n);
    }
}
