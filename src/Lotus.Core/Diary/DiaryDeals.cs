using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Catalog;

namespace Lotus.Core.Diary;

// Games on the "Quero jogar" list that are on sale on Steam right now (price from the store, never guessed).
public sealed record DealRow(DiaryEntry Entry, int Discount, string Final, string Initial)
{
    public string Name => Entry.Name;
    public string? Cover => Entry.Cover;
    public string DiscountText => "-" + Discount.ToString(CultureInfo.InvariantCulture) + "%";
    public bool HasInitial => Initial.Length > 0;
    public string StoreUrl => "https://store.steampowered.com/app/" + Entry.AppId;
}

public sealed class DiaryDealsViewModel(GameDiary diary, IGameCatalog catalog, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public IReadOnlyList<DealRow> Deals { get; private set; } = [];
    public bool HasDeals => Deals.Count > 0;
    public string Title => Text.Format(Deals.Count == 1 ? "DlTitleOne" : "DlTitle", Deals.Count);
    private DateTimeOffset checkedAt = DateTimeOffset.MinValue;
    private readonly HashSet<string> told = [];
    private bool busy;

    public static IReadOnlyList<DealRow> Pick(IEnumerable<DiaryEntry> entries, IReadOnlyDictionary<string, GamePrice> prices) =>
        [.. entries.Where(e => e.Status == DiaryStatus.Want && e.AppId is { } id && prices.TryGetValue(id, out var p) && p.Discount > 0)
            .Select(e => { var p = prices[e.AppId!]; return new DealRow(e, p.Discount, p.Final, p.Initial); })
            .OrderByDescending(d => d.Discount).ThenBy(d => d.Name, StringComparer.CurrentCulture)];

    // At most every 30 minutes unless forced (prices are cached for hours anyway). Returns the deals not shown before.
    public async Task<IReadOnlyList<DealRow>> RefreshAsync(bool force = false, CancellationToken token = default)
    {
        if (busy || (!force && DateTimeOffset.Now - checkedAt < TimeSpan.FromMinutes(30))) return [];
        busy = true;
        try
        {
            var ids = diary.Entries.Where(e => e.Status == DiaryStatus.Want && e.AppId is not null).Select(e => e.AppId!).ToList();
            var prices = ids.Count == 0 ? new Dictionary<string, GamePrice>() : await catalog.PricesAsync(ids, token);
            checkedAt = DateTimeOffset.Now;
            Deals = Pick(diary.Entries, prices);
            foreach (var n in new[] { nameof(Deals), nameof(HasDeals), nameof(Title) }) OnPropertyChanged(n);
            return [.. Deals.Where(d => told.Add(d.Entry.AppId + ":" + d.Discount))];
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); return []; }
        finally { busy = false; }
    }

    // The list changed (a game added to or removed from "Quero jogar"): look again next time.
    public void Invalidate() => checkedAt = DateTimeOffset.MinValue;
}
