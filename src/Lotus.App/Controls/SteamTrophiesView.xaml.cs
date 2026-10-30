using Lotus.Core.Achievements;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// "Steam" section of the diary: every played game with its achievements, read from Steam on this PC.
public sealed partial class SteamTrophiesView : UserControl
{
    private AchievementsViewModel? Vm => DataContext as AchievementsViewModel;
    public SteamTrophiesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (Vm is { } vm) { vm.PropertyChanged -= OnVm; vm.PropertyChanged += OnVm; Paint(); } };
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AchievementsViewModel.HasSelected) or nameof(AchievementsViewModel.HasSteam)) DispatcherQueue.TryEnqueue(Paint);
    }

    private void Paint()
    {
        if (Vm is not { } vm) return;
        GamesArea.Visibility = vm.HasSteam && !vm.HasSelected ? Visibility.Visible : Visibility.Collapsed;
        DetailArea.Visibility = vm.HasSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGame(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: SteamGameCard game }) return;
        UiSound.Play(UiSound.Cue.Open);
        vm.Select(game);
        Motion.EnterPage(DetailArea);
        BringTop();
    }
    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        vm.Select(null);
        Motion.EnterPage(GamesArea);
        BringTop();
    }
    private async void OnSync(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Motion.Pop(SyncIcon); UiSound.Play(UiSound.Cue.Toggle);
        await vm.Center.SyncAsync();
    }
    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (Vm is { } vm && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) vm.SteamQuery = sender.Text; }

    // Opening or leaving a game scrolls the page back to this section's top.
    private void BringTop() => StartBringIntoView(new BringIntoViewOptions { AnimationDesired = Motion.Active, VerticalAlignmentRatio = 0 });
}
