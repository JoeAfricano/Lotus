namespace Lotus.Core.Diary;

// "Quanto tempo leva para zerar": community times from HowLongToBeat (main story, story + extras, 100%), kept on the
// diary entry so the diary, the editor and "O que jogar agora?" can use them offline. Looked up quietly, a few games
// at a time, only for games the player is playing or wants to play; a game the site does not know is asked again later.
public sealed record GameLength(int? MainMinutes, int? PlusMinutes, int? AllMinutes, string? Url = null)
{
    public bool HasAny => MainMinutes is > 0 || PlusMinutes is > 0 || AllMinutes is > 0;
}

public interface IGameLengthSource
{
    // The game with this Steam id (when known) or exactly this title; null when not found (never a "close enough" game).
    Task<GameLength?> FindAsync(string name, string? steamAppId, CancellationToken token);
}

public sealed class DiaryLengths(GameDiary diary, IGameLengthSource source, Action<Exception> log)
{
    public const int PerRun = 12;                                   // lookups per start: gentle with the site
    public static readonly TimeSpan FoundAge = TimeSpan.FromDays(90), MissAge = TimeSpan.FromDays(14);
    public TimeSpan Pause { get; set; } = TimeSpan.FromSeconds(2);

    public static bool Wanted(DiaryEntry e, DateTimeOffset now) =>
        e.Status is DiaryStatus.Playing or DiaryStatus.Paused or DiaryStatus.Want
        && (e.BeatChecked is not { } at || now - at > (e.BeatMain is > 0 || e.BeatAll is > 0 ? FoundAge : MissAge));

    public async Task<int> RefreshAsync(CancellationToken cancel = default)
    {
        var now = DateTimeOffset.Now; var found = 0; var asked = 0;
        foreach (var e in diary.Entries.Where(e => Wanted(e, now)).OrderBy(e => e.Status == DiaryStatus.Playing ? 0 : 1).ThenByDescending(e => e.Updated).Take(PerRun).ToList())
        {
            if (asked++ > 0) await Task.Delay(Pause, cancel);
            if (await LookUpAsync(e.Key, cancel) is { HasAny: true }) found++;
        }
        return found;
    }

    // One game now (the editor opened it): uses what is saved when it is recent enough.
    public async Task<GameLength?> LookUpAsync(string key, CancellationToken cancel = default)
    {
        if (diary.Get(key) is not { } e) return null;
        if (e.BeatChecked is { } at && DateTimeOffset.Now - at <= (e.BeatMain is > 0 || e.BeatAll is > 0 ? FoundAge : MissAge)) return Saved(e);
        try
        {
            var length = await source.FindAsync(e.Name, e.AppId, cancel);
            diary.SetLength(key, length);
            return length;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); return null; } // offline / site changed: tried again next start
    }

    public static GameLength? Saved(DiaryEntry e) => e.BeatMain is > 0 || e.BeatPlus is > 0 || e.BeatAll is > 0 ? new(e.BeatMain, e.BeatPlus, e.BeatAll, e.BeatUrl) : null;
}
