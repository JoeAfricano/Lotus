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
        HeroArt.Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Concept", "lotus-sculpture.png"))) { DecodePixelWidth = 1200 };
        Loaded += OnLoaded;
        Unloaded += (_, _) => refresh?.Stop();
    }
    private GameListViewModel? Vm => DataContext as GameListViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookScroll();
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

    // Clearing restores every list filter / the draw filters; nothing else (search, chart) changes.
    private void OnClearFilters(object sender, RoutedEventArgs e) => Vm?.ClearFilters();

    // The kinds of game window (several at once, "any" or "all").
    private async void OnPickTypes(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var picker = new GameTypePicker(vm.Text, vm.SelectedTypes, vm.MatchAll);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = vm.Text["GtTitle"], Content = picker,
            PrimaryButtonText = vm.Text["GtApply"], SecondaryButtonText = vm.Text["GtClear"], CloseButtonText = vm.Text["DlgCancel"], DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 980.0;
        dialog.SecondaryButtonClick += (_, args) => { args.Cancel = true; picker.Clear(); }; // "Clear" keeps the window open
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        UiSound.Play(UiSound.Cue.Done);
        await vm.SetTypesAsync(picker.Selected, picker.MatchAll);
    }
    private void OnClearPick(object sender, RoutedEventArgs e) { DiffBox.SelectedIndex = 0; }
    private void OnChart(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) { if (Vm is { } vm && sender.SelectedItem?.Tag is string id) vm.Chart = id; }
    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (Vm is { } vm && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) vm.Query = sender.Text; }
    private void OnSearchSubmit(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) { if (Vm is { } vm) vm.Query = sender.Text; }
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
            ShowCover(vm.Details.Cover is { Length: > 0 } art ? art : p.Wide);
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
    private void ShowCover(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var u)) { ShuffleArt.Source = new BitmapImage(u) { DecodePixelWidth = 280 }; ShuffleIdle.Visibility = Visibility.Collapsed; } }

    // Covers load in steps: portrait art → wide header → the exact art path from the store API (newer games keep
    // their art under hashed paths). The grid recycles cards, so each step checks the card still shows that game.
    private static readonly Dictionary<string, string> artCache = [];
    private void OnCoverContext(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not Image img) return;
        img.Opacity = 0; img.Source = null;
        if (args.NewValue is CatalogCard card) Load(img, artCache.GetValueOrDefault(card.Item.AppId) ?? card.Cover);
    }
    private void OnCoverOpened(object sender, RoutedEventArgs e) { if (sender is Image img) { img.Opacity = 1; } }
    private async void OnCoverFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not Image { DataContext: CatalogCard card } img) return;
        var tried = (img.Source as BitmapImage)?.UriSource?.ToString() ?? "";
        if (tried == card.Cover) { Load(img, card.Wide); return; }
        if (Vm is { } vm && await vm.ArtAsync(card.Item.AppId) is { } art && art != tried && img.DataContext == card)
        {
            artCache[card.Item.AppId] = art; Load(img, art);
        }
        // Otherwise the name stays on the plum card underneath.
    }
    // Covers decode at card size (not the full 600x900 art): far less memory with long lists.
    private static void Load(Image img, string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var u)) img.Source = new BitmapImage(u) { DecodePixelWidth = 220, DecodePixelType = DecodePixelType.Logical }; }
    private void OnShowMore(object sender, RoutedEventArgs e) => Vm?.ShowMore();
    // Endless list: the next page arrives when the page scroll gets close to the end.
    private ScrollViewer? pageScroll;
    private void HookScroll()
    {
        if (pageScroll is not null) return;
        for (var p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this); p is not null; p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer sv) { pageScroll = sv; break; }
        if (pageScroll is not null) pageScroll.ViewChanged += (s, _) =>
        {
            if (ListArea.Visibility == Visibility.Visible && Vm is { CanShowMore: true } vm && pageScroll.ScrollableHeight - pageScroll.VerticalOffset < 700) vm.ShowMore();
        };
    }

    // Star on a cover: adds the game to "Quero jogar" in the diary (the star fills in).
    private void OnWant(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CatalogCard card } b || MainWindow.Shell is not { } shell) return;
        var game = new Lotus.Core.Diary.DiaryEntry { Key = Lotus.Core.Diary.DiaryEntry.SteamKey(card.Item.AppId), AppId = card.Item.AppId, Name = card.Name, Cover = card.Cover, Wide = card.Wide };
        if (!shell.WantGame(game)) { shell.OpenDiaryEditor(game); return; }
        if (b.Content is FontIcon icon) icon.Glyph = "";
        Motion.Pop(b);
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
