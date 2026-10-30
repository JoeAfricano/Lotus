using Lotus.Core.Catalog;
using Lotus.Core.Games;

namespace Lotus.Core.Diary;

public sealed record FinishedSession(GameEntry Game, DateTimeOffset Start, int Minutes);

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
        if (game is not null && (running is null || !string.Equals(running.Key, game.Key, StringComparison.OrdinalIgnoreCase) || now - seen > Gap)) done = Finish();
        if (running is not null) { if (game is null) { game = running; start = now; } seen = now; }
        return done;
    }

    // Lotus is closing: the session so far still counts.
    public FinishedSession? Flush() => game is null ? null : Finish();

    private FinishedSession? Finish()
    {
        var g = game!; game = null;
        var minutes = (int)Math.Round((seen - start).TotalMinutes);
        return minutes >= MinMinutes ? new(g, start, minutes) : null;
    }
}

public static class DiaryGames
{
    // The diary key for an installed game: Steam games use their app id (same entry as the Game list), others the
    // launcher key ("Epic Games:<name>", "GOG:<id>"...).
    public static string KeyFor(GameEntry g) => g.Store == GameStores.Steam ? DiaryEntry.SteamKey(g.Id) : g.Key;

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
