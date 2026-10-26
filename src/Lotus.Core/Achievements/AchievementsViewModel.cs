using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Achievements;

public sealed record TrophyCard(string Id, string Name, string Detail, string Glyph, string Tier, string TierLabel, string XpText, bool Unlocked,
    bool Masked, string ProgressText, double Percent, string DateText, string Category);
public sealed record CategoryTab(string Id, string Label, string CountText);
public sealed record SteamGameCard(SteamGameAchievements Game, string Name, string Header, string CountText, double Percent, string PercentText, string LastText, bool Perfect);
public sealed record SteamAchievementCard(string Name, string Description, string Icon, bool Unlocked, string RarityKind, string RarityText, string DateText, string Game);

// "Conquistas" in the diary: Lotus achievements by category, what is nearly done, the rarest Steam unlocks and every
// Steam game with its achievements.
public sealed class AchievementsViewModel : ObservableObject
{
    private readonly TrophyCenter center;
    private string category = "all", steamQuery = "", steamSort = "recent";
    private SteamGameCard? selected;

    public AchievementsViewModel(TrophyCenter center, ITextCatalog text)
    {
        this.center = center; Text = text;
        center.Changed += (_, _) => Refresh();
        Refresh();
    }
    public ITextCatalog Text { get; }
    public TrophyCenter Center => center;
    public ObservableCollection<CategoryTab> Tabs { get; } = [];
    public ObservableCollection<TrophyCard> Cards { get; } = [];
    public IReadOnlyList<TrophyCard> NextUp { get; private set; } = [];
    public IReadOnlyList<SteamAchievementCard> Showcase { get; private set; } = [];
    public ObservableCollection<SteamGameCard> SteamGames { get; } = [];
    public IReadOnlyList<SteamAchievementCard> SelectedItems { get; private set; } = [];
    public IReadOnlyList<CategoryTab> SteamSorts => [.. new[] { "recent", "progress", "name" }.Select(s => new CategoryTab(s, Text["TrSort" + s], ""))];

    private CultureInfo Culture => CultureInfo.GetCultureInfo(Text.Language);
    private string N(int n) => n.ToString("N0", Culture);
    public int UnlockedCount => center.States.Count(s => s.Unlocked);
    public string SummaryText => Text.Format("TrSummary", UnlockedCount, center.States.Count);
    public string XpText => Text.Format("TrXpTotal", N(center.ExtraXp));
    public double SummaryPercent => center.States.Count == 0 ? 0 : UnlockedCount * 100.0 / center.States.Count;
    public bool HasNextUp => NextUp.Count > 0;
    public bool HasShowcase => Showcase.Count > 0;

    public bool HasSteam => center.HasSteam;
    public bool NoSteam => !center.HasSteam && !center.Syncing;
    public bool Syncing => center.Syncing;
    public string SteamUnlockedText => N(center.Steam.Sum(g => g.Unlocked));
    public string SteamPerfectText => N(center.Steam.Count(g => g.Perfect));
    public string SteamGamesText => N(center.Steam.Count(g => g.Unlocked > 0));
    public string SteamXpText => N(center.SteamXp);
    public string SyncText => center.Syncing ? Text["TrSyncing"] : center.LastSync is { } t ? Text.Format("TrSynced", t.ToLocalTime().ToString("t", Culture)) : Text["TrNeverSynced"];
    public SteamGameCard? Selected { get => selected; private set { if (SetProperty(ref selected, value)) { OnPropertyChanged(nameof(HasSelected)); OnPropertyChanged(nameof(SelectedItems)); } } }
    public bool HasSelected => selected is not null;

    public string Category { get => category; set { if (value is not null && SetProperty(ref category, value)) FillCards(); } }
    public string SteamQuery { get => steamQuery; set { if (SetProperty(ref steamQuery, value ?? "")) FillSteam(); } }
    public string SteamSort { get => steamSort; set { if (value is not null && SetProperty(ref steamSort, value)) FillSteam(); } }

    public void Refresh()
    {
        var states = center.States;
        Tabs.Clear();
        Tabs.Add(new("all", Text["TrCatall"], $"{states.Count(s => s.Unlocked)}/{states.Count}"));
        foreach (var c in Enum.GetValues<TrophyCategory>())
        {
            var inCat = states.Where(s => s.Def.Category == c).ToList();
            Tabs.Add(new(c.ToString().ToLowerInvariant(), Text["TrCat" + c.ToString().ToLowerInvariant()], $"{inCat.Count(s => s.Unlocked)}/{inCat.Count}"));
        }
        NextUp = [.. states.Where(s => !s.Unlocked && !s.Def.Secret && s.Fraction > 0).OrderByDescending(s => s.Fraction).ThenBy(s => s.Def.Xp).Take(3).Select(Card)];
        Showcase = [.. center.Steam.SelectMany(g => g.Items.Where(a => a.Unlocked && a.Percent is not null).Select(a => (g, a)))
            .OrderBy(x => x.a.Percent).Take(3).Select(x => SteamCard(x.g, x.a))];
        FillCards();
        FillSteam();
        if (selected is not null) Select(SteamGames.FirstOrDefault(g => g.Game.AppId == selected.Game.AppId));
        foreach (var n in new[] { nameof(NextUp), nameof(HasNextUp), nameof(Showcase), nameof(HasShowcase), nameof(SummaryText), nameof(XpText), nameof(SummaryPercent), nameof(UnlockedCount),
            nameof(HasSteam), nameof(NoSteam), nameof(Syncing), nameof(SteamUnlockedText), nameof(SteamPerfectText), nameof(SteamGamesText), nameof(SteamXpText), nameof(SyncText) })
            OnPropertyChanged(n);
    }

    public void Select(SteamGameCard? game)
    {
        SelectedItems = game is null ? [] : [.. game.Game.Items
            .OrderByDescending(a => a.Unlocked).ThenByDescending(a => a.UnlockedAt).ThenBy(a => a.Percent ?? 101)
            .Select(a => SteamCard(game.Game, a))];
        Selected = game;
        OnPropertyChanged(nameof(SelectedItems));
    }

    private void FillCards()
    {
        IEnumerable<TrophyState> rows = center.States;
        if (category != "all") rows = rows.Where(s => s.Def.Category.ToString().Equals(category, StringComparison.OrdinalIgnoreCase));
        rows = rows.OrderByDescending(s => s.Unlocked).ThenByDescending(s => s.Unlocked ? s.UnlockedAt?.UtcTicks ?? 0 : (long)(s.Fraction * 1000)).ThenBy(s => s.Def.Tier);
        Cards.Clear();
        foreach (var s in rows) Cards.Add(Card(s));
    }

    private void FillSteam()
    {
        IEnumerable<SteamGameAchievements> games = center.Steam;
        if (steamQuery.Trim() is { Length: > 0 } q) games = games.Where(g => g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        games = steamSort switch
        {
            "progress" => games.OrderByDescending(g => g.Fraction).ThenByDescending(g => g.Unlocked),
            "name" => games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => games.OrderByDescending(g => g.LastUnlock ?? DateTimeOffset.MinValue).ThenByDescending(g => g.Unlocked),
        };
        SteamGames.Clear();
        foreach (var g in games)
            SteamGames.Add(new(g, g.Name, SteamGameAchievements.HeaderUrl(g.AppId), Text.Format("TrGameCount", g.Unlocked, g.Total), g.Fraction * 100,
                (g.Fraction * 100).ToString("0", Culture) + "%", g.LastUnlock is { } l ? Text.Format("TrLastUnlock", l.ToLocalTime().ToString("d", Culture)) : Text["TrNoUnlock"], g.Perfect));
    }

    private TrophyCard Card(TrophyState s)
    {
        var d = s.Def;
        var masked = d.Secret && !s.Unlocked;
        var tier = d.Tier.ToString().ToLowerInvariant();
        return new(d.Id, masked ? Text["TrSecretName"] : Text[d.NameKey], masked ? Text["TrSecretHint"] : Text[d.HintKey], masked ? "" : d.Glyph,
            tier, Text["TrTier" + tier], "+" + d.Xp + " XP", s.Unlocked, masked,
            s.Unlocked ? (s.UnlockedAt is { } when ? Text.Format("TrEarnedOn", when.ToLocalTime().ToString("d", Culture)) : Text["DyBadgeDone"]) : $"{N(s.Current)}/{N(s.Goal)}", s.Fraction * 100,
            s.UnlockedAt is { } at ? Text.Format("TrEarnedOn", at.ToLocalTime().ToString("d", Culture)) : "", d.Category.ToString().ToLowerInvariant());
    }

    private SteamAchievementCard SteamCard(SteamGameAchievements g, SteamAchievement a)
    {
        var hidden = a.Hidden && !a.Unlocked;
        var kind = Rarity.Kind(a.Percent);
        var rarity = a.Percent is { } p ? Text.Format("TrRarity", Text["TrRarity" + kind], p.ToString(p < 10 ? "0.0" : "0", Culture)) : "";
        return new(hidden ? Text["TrHiddenName"] : a.Name, hidden ? Text["TrHiddenHint"] : a.Description,
            SteamGameAchievements.IconUrl(g.AppId, a.Unlocked ? a.Icon : a.IconGray ?? a.Icon), a.Unlocked, kind, rarity,
            a.UnlockedAt is { } at ? at.ToLocalTime().ToString("d", Culture) : "", g.Name);
    }
}
