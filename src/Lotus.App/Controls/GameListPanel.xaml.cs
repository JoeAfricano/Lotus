using Lotus.Core.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// Game list tab. Charts refresh on their own every six hours while Lotus is open; "draw a game" flips through covers
// before landing on the pick; a click opens the game page in place.
public sealed partial class GameListPanel : UserControl
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? refresh;
    public event EventHandler<Lotus.Core.Games.GameEntry>? PlayRequested;
    public GameListPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => refresh?.Stop();
    }
    private GameListViewModel? Vm => DataContext as GameListViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (ChartBar.Items.Count == 0)
        {
            foreach (var tab in vm.Tabs) ChartBar.Items.Add(new SelectorBarItem { Text = tab.Label, Tag = tab.Id });
            ChartBar.SelectedItem = ChartBar.Items.FirstOrDefault(i => (string)i.Tag == vm.Chart);
            foreach (var d in new[] { "any", "easy", "medium", "hard" }) DiffBox.Items.Add(new ComboBoxItem { Content = vm.Text["GlDiff" + d], Tag = d });
            DiffBox.SelectedIndex = 0;
        }
        refresh = DispatcherQueue.CreateTimer(); refresh.Interval = TimeSpan.FromMinutes(30);
        refresh.Tick += async (_, _) => { if (Vm is { } v) await v.RefreshIfStaleAsync(); };
        refresh.Start();
        if (vm.PendingOpen is { } pending) { vm.PendingOpen = null; ShowDetail(true); _ = vm.OpenAsync(pending); }
        await vm.ShowAsync();
    }

    private void OnChart(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) { if (Vm is { } vm && sender.SelectedItem?.Tag is string id) vm.Chart = id; }
    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (Vm is { } vm && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) vm.Query = sender.Text; }
    private void OnGenre(object sender, SelectionChangedEventArgs e) { if (Vm is { } vm && GenreBox.SelectedValue is string g) vm.Genre = g; }
    private void OnDifficulty(object sender, SelectionChangedEventArgs e) { if (Vm is { } vm && DiffBox.SelectedItem is ComboBoxItem { Tag: string d }) vm.DifficultyWanted = d; }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Motion.Pop(RefreshIcon);
        await vm.ShowAsync(force: true);
    }

    private async void OnItem(object sender, ItemClickEventArgs e)
    {
        if (Vm is not { } vm || e.ClickedItem is not CatalogCard card) return;
        ShowDetail(true);
        await vm.OpenAsync(card.Item);
    }

    // Draw: covers of random list games flicker in the frame, then the page of the chosen one opens.
    private async void OnPick(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.Picking) return;
        Motion.Pop(DiceIcon);
        var shuffle = vm.Items.Count > 0 ? vm.Items.Select(i => i.Cover).OrderBy(_ => Random.Shared.Next()).Take(10).ToList() : [];
        var spin = shuffle.Count == 0 ? Task.CompletedTask : Shuffle(shuffle);
        var pick = vm.PickAsync();
        await Task.WhenAll(spin, pick);
        if (vm.Picked is { } p)
        {
            ShowCover(p.Cover);
            UiSound.Play(UiSound.Cue.Score);
            PickLabel.Text = vm.Text["GlPickAgain"];
            ShowDetail(true);
        }
        else UiSound.Play(UiSound.Cue.Fail);
    }
    private async Task Shuffle(IReadOnlyList<string> covers)
    {
        ShuffleIdle.Visibility = Visibility.Collapsed;
        for (var i = 0; i < covers.Count; i++) { ShowCover(covers[i]); Motion.Pop(ShuffleArt); await Task.Delay(90 + i * 18); }
    }
    private void ShowCover(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var u)) { ShuffleArt.Source = new BitmapImage(u); ShuffleIdle.Visibility = Visibility.Collapsed; } }

    // Some apps have no portrait art: fall back to the wide header.
    private void OnCoverFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image { Tag: string wide } img && Uri.TryCreate(wide, UriKind.Absolute, out var u) && (img.Source as BitmapImage)?.UriSource != u) img.Source = new BitmapImage(u);
    }

    private void OnBack(object? sender, EventArgs e) => ShowDetail(false);
    private void OnPlay(object? sender, Lotus.Core.Games.GameEntry game) => PlayRequested?.Invoke(this, game);

    private void ShowDetail(bool on)
    {
        ListArea.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Detail.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Motion.EnterPage(on ? Detail : ListArea);
    }
}
