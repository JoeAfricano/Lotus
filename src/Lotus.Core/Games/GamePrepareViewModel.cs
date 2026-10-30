using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Games;

// The "Preparar e Jogar" card on a game's page: a few switches, saved per game. Turning on the Game profile is
// confirmed by the app (it lists what changes) before it is stored as authorized.
public sealed class GamePrepareViewModel(GamePrefsBook book, GameEntry game, ITextCatalog text, Func<bool> globalDefault) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public GameEntry Game { get; } = game;
    private GamePlayPrefs P => book.Get(Game.Key);
    private void Save(GamePlayPrefs next, params string[] names) { if (book.Set(Game.Key, next)) foreach (var n in names) OnPropertyChanged(n); }

    public bool Prepare { get => P.Prepare ?? globalDefault(); set { if (value != Prepare) Save(P with { Prepare = value }, nameof(Prepare), nameof(Summary), nameof(DetailsEnabled)); } }
    public bool DetailsEnabled => Prepare;
    public bool UseProfile { get => P.UseProfile; set { if (value != P.UseProfile) Save(P with { UseProfile = value }, nameof(UseProfile), nameof(Summary)); } }
    public bool CheckController { get => P.CheckController; set { if (value != P.CheckController) Save(P with { CheckController = value }, nameof(CheckController), nameof(Summary)); } }
    public bool CheckSystem { get => P.CheckSystem; set { if (value != P.CheckSystem) Save(P with { CheckSystem = value }, nameof(CheckSystem), nameof(Summary)); } }
    public bool OnlyRelevant { get => P.OnlyRelevant; set { if (value != P.OnlyRelevant) Save(P with { OnlyRelevant = value }, nameof(OnlyRelevant), nameof(Summary)); } }

    public string Summary => !Prepare ? Text["PrepSummaryOff"]
        : UseProfile ? Text["PrepSummaryProfile"]
        : Text["PrepSummaryOn"];
}
