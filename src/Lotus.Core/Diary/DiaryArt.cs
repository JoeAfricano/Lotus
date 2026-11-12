using System.Globalization;
using System.Text;
using Lotus.Core.Catalog;

namespace Lotus.Core.Diary;

// Covers that never showed on Home, in the diary and in the player panel: entries saved without a cover (games from
// other launchers, added by hand, started by a session) or with the guessed Steam portrait, which newer games no
// longer have (their art sits under a hashed path only the store API knows). Repairs them once, quietly:
//   1. a Steam id → the exact portrait from the store (one batched request, cached on disk by the catalog);
//   2. the cover the library already shows for that game (Xbox logo, the player's own picture…);
//   3. no id → the Steam store by name, only on an exact title match (never a "close enough" game).
// Only the art changes: status, dates and the "updated" order of the shelf stay as they were. Awaits resume on the
// caller's thread on purpose: the diary raises Changed and the UI repaints from it.
public sealed class DiaryArt(GameDiary diary, IGameCatalog catalog, Action<Exception> log)
{
    public const int SearchLimit = 25; // name searches per run: the rest waits for the next start
    private readonly HashSet<string> tried = new(StringComparer.OrdinalIgnoreCase);

    // Library covers by diary key (set by the app: installed games, the player's own pictures).
    public Func<string, string?>? LibraryCover { get; set; }
    // A local cover file still there? (replaced in tests)
    public Func<string, bool> FileExists { get; set; } = File.Exists;

    public static bool IsGuessedPortrait(string? cover, string? appId) => appId is { Length: > 0 } && cover == CatalogText.Portrait(appId);

    // Entries whose cover is missing, a local file that is gone, or the guessed Steam portrait (checked against the store).
    public bool NeedsArt(DiaryEntry e) =>
        e.Cover is not { Length: > 0 } c || IsGuessedPortrait(c, e.AppId) || (!c.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !FileExists(c));

    public async Task<int> RepairAllAsync(CancellationToken cancel = default)
    {
        var fixedCount = 0; var searches = 0;
        foreach (var e in diary.Entries.Where(NeedsArt).OrderBy(e => e.Status is DiaryStatus.Playing or DiaryStatus.Want ? 0 : 1).ToList())
        {
            cancel.ThrowIfCancellationRequested();
            var needsSearch = e.AppId is not { Length: > 0 } && LibraryCover?.Invoke(e.Key) is not { Length: > 0 };
            if (needsSearch && searches >= SearchLimit) continue;
            if (needsSearch) searches++;
            if (await RepairAsync(e.Key, broken: null, cancel)) fixedCount++;
        }
        return fixedCount;
    }

    // One entry (also called when an image fails to load on screen: broken = the address that failed).
    public async Task<bool> RepairAsync(string key, string? broken, CancellationToken cancel = default)
    {
        if (diary.Get(key) is not { } e) return false;
        if (broken is null && !NeedsArt(e)) return false;
        if (!tried.Add(key + "|" + (broken ?? e.Cover ?? ""))) return false; // each failure is looked up once per run
        try
        {
            string? cover = null, wide = null;
            if (e.AppId is { Length: > 0 } id)
            {
                cover = await catalog.PortraitAsync(id, cancel);
                if (cover is null && broken is not null) cover = await catalog.ArtAsync(id, cancel); // the portrait is gone: the header still shows the game
            }
            if (cover is null && LibraryCover?.Invoke(e.Key) is { Length: > 0 } library && library != broken) cover = library;
            if (cover is null && e.AppId is not { Length: > 0 })
            {
                var wanted = Normalize(e.Name);
                if (wanted.Length >= 2)
                {
                    var hit = (await catalog.SearchAsync(e.Name, cancel)).FirstOrDefault(i => Normalize(i.Name) == wanted);
                    if (hit is not null)
                    {
                        cover = await catalog.PortraitAsync(hit.AppId, cancel) ?? hit.Cover;
                        wide = e.Wide is { Length: > 0 } ? null : hit.Wide is { Length: > 0 } w ? w : CatalogText.Header(hit.AppId);
                    }
                }
            }
            if (cover is not { Length: > 0 } || cover == e.Cover || cover == broken) return false;
            return diary.SetArt(e.Key, cover, wide);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); return false; }
    }

    // "Baldur's Gate 3™" = "baldursgate3": case, accents, symbols and spacing do not matter; words do.
    public static string Normalize(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
