using Lotus.Core.Catalog;
using Lotus.Core.Games;

namespace Lotus.Core.Diary;

// How a session ended: the game closed (normal), another game took its place, Lotus stopped seeing it for a while
// (sleep, hang) or Lotus itself closed. Only a normal close counts as "the player just stopped playing".
public enum SessionEnd { Closed, Switched, Gap, LotusClosing }
public sealed record FinishedSession(GameEntry Game, DateTimeOffset Start, int Minutes, SessionEnd End = SessionEnd.Closed)
{
    public bool EndedNormally => End == SessionEnd.Closed;
}

// Turns "which library game is open now" (checked every few seconds) into play sessions. A session ends when the game
// closes, another game takes its place, or Lotus stops seeing it for a while (sleep, Lotus closed); the end is the last
// time it was seen, so a sleeping PC never counts as play time. Very short sessions (crash, quick launch) are ignored.
public sealed class PlaySessionTracker
{
    public static readonly TimeSpan Gap = TimeSpan.FromMinutes(3);
    public const int MinMinutes = 2;
    private GameEntry? game;
    private DateTimeOffset start, seen;
    public GameEntry? Current => game;
    public DateTimeOffset? StartedAt => game is null ? null : start;

    public FinishedSession? Update(GameEntry? running, DateTimeOffset now)
    {
        FinishedSession? done = null;
        if (game is not null)
        {
            if (now - seen > Gap) done = Finish(SessionEnd.Gap);
            else if (running is null) done = Finish(SessionEnd.Closed);
            else if (!string.Equals(running.Key, game.Key, StringComparison.OrdinalIgnoreCase)) done = Finish(SessionEnd.Switched);
        }
        if (running is not null) { if (game is null) { game = running; start = now; } seen = now; }
        return done;
    }

    // Lotus is closing: the session so far still counts.
    public FinishedSession? Flush() => game is null ? null : Finish(SessionEnd.LotusClosing);

    private FinishedSession? Finish(SessionEnd end)
    {
        var g = game!; game = null;
        var minutes = (int)Math.Round((seen - start).TotalMinutes);
        return minutes >= MinMinutes ? new(g, start, minutes, end) : null;
    }
}

public static class DiaryGames
{
    // The diary key for an installed game: Steam games use their app id (same entry as the Game list), others the
    // launcher key ("Epic Games:<name>", "GOG:<id>"...).
    public static string KeyFor(GameEntry g) => g.Store == GameStores.Steam ? DiaryEntry.SteamKey(g.Id) : g.Key;
    // Same rule from a library key ("Steam:<appid>", "Epic Games:<name>"...), for data kept by library key (player panel).
    public static string KeyFor(string libraryKey) =>
        libraryKey.StartsWith(GameStores.Steam + ":", StringComparison.OrdinalIgnoreCase) ? DiaryEntry.SteamKey(libraryKey[(GameStores.Steam.Length + 1)..]) : libraryKey;

    public static DiaryEntry FromGame(GameEntry g)
    {
        var steam = g.Store == GameStores.Steam ? g.Id : null;
        return new()
        {
            Key = KeyFor(g), Name = g.Name, AppId = steam,
            Cover = steam is not null ? CatalogText.Portrait(steam) : g.CoverPath,
            Wide = steam is not null ? CatalogText.Header(steam) : null,
        };
    }
}
