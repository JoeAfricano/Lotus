using Lotus.Core.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        if (ChartTiles.Children.Count == 0)
        {
            BuildChartTiles(vm);
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
        picker.FitTo(XamlRoot.Size.Height);
        dialog.SecondaryButtonClick += (_, args) => { args.Cancel = true; picker.Clear(); }; // "Clear" keeps the window open
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        UiSound.Play(UiSound.Cue.Done);
        await vm.SetTypesAsync(picker.Selected, picker.MatchAll);
    }
    private void OnClearPick(object sender, RoutedEventArgs e) { DiffBox.SelectedIndex = 0; }
    // ---- Category tabs (José's "Destaque + abas"): icon, name and how many games; the chosen one is lit ----
    private static readonly Dictionary<string, string> ChartGlyphs = new() { ["trending"] = "", ["toprated"] = "", ["topsellers"] = "", ["new"] = "", ["upcoming"] = "" };
    private void BuildChartTiles(GameListViewModel vm)
    {
        ChartTiles.Children.Clear(); ChartTiles.ColumnDefinitions.Clear();
        foreach (var tab in vm.Tabs)
        {
            ChartTiles.ColumnDefinitions.Add(new ColumnDefinition());
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(new FontIcon { Glyph = ChartGlyphs.GetValueOrDefault(tab.Id, ""), FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = tab.Label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold } });
            var tile = new Button { Content = row, Tag = tab.Id, Padding = new Thickness(16, 10, 16, 10), MinHeight = 46, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            Motion.SetPress(tile, true);
            ToolTipService.SetToolTip(tile, vm.Text["GlHint" + tab.Id]);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, tab.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tile, "gamelist-chart-" + tab.Id);
            tile.Click += (_, _) => { if (Vm is { } v) { switchClock.Restart(); v.Chart = tab.Id; PaintChartTiles(v); } };
            Grid.SetColumn(tile, ChartTiles.Children.Count);
            ChartTiles.Children.Add(tile);
        }
        foreach (var id in new[] { "week", "month", "year", "all" })
        {
            var b = new Button { Content = vm.Text["GlPeriod" + id], Tag = id, Padding = new Thickness(14, 6, 14, 6), MinHeight = 34, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, "gamelist-period-" + id);
            b.Click += (_, _) => { if (Vm is { } v) { switchClock.Restart(); v.Period = id; PaintPeriod(v); } };
            PeriodBar.Children.Add(b);
        }
        PeriodLabel.Text = vm.Text["GlPeriodLabel"];
        PaintChartTiles(vm); PaintPeriod(vm);
        // One repaint per change (a list reset is a single event); the timing is logged to compare tab switches.
        vm.Items.CollectionChanged += (_, _) =>
        {
            if (repaintQueued) return;
            repaintQueued = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                repaintQueued = false;
                if (Vm is not { } v) return;
                PaintChartTiles(v); PaintFeatured(v); PaintPeriod(v);
                if (switchClock.IsRunning) { Serilog.Log.Information("Game list switch {Chart}/{Period} shown in {Ms} ms ({Count} cards)", v.Chart, v.Period, switchClock.ElapsedMilliseconds, v.Items.Count); switchClock.Reset(); }
            });
        };
    }
    private bool repaintQueued;
    private readonly System.Diagnostics.Stopwatch switchClock = new();
    private void PaintPeriod(GameListViewModel vm)
    {
        var res = Application.Current.Resources;
        foreach (var b in PeriodBar.Children.OfType<Button>())
        {
            var on = (string)b.Tag == vm.Period;
            b.IsEnabled = vm.PeriodAvailable || (string)b.Tag == "all";
            b.Background = on ? (Brush)res["AccentGradientBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            b.Foreground = (Brush)res[on ? "OnRoseBrush" : "MainTextBrush"];
            ToolTipService.SetToolTip(b, vm.PeriodAvailable ? vm.Text["GlPeriodHint" + (vm.Chart == "upcoming" ? "Soon" : "")] : vm.Text["GlPeriodNoDates"]);
        }
    }
    private void PaintChartTiles(GameListViewModel vm)
    {
        var res = Application.Current.Resources;
        foreach (var tile in ChartTiles.Children.OfType<Button>())
        {
            var on = (string)tile.Tag == vm.Chart;
            var row = (StackPanel)tile.Content;
            tile.Background = on ? (Brush)res["AccentGradientBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var fg = (Brush)res[on ? "OnRoseBrush" : "MainTextBrush"];
            ((FontIcon)row.Children[0]).Foreground = on ? fg : (Brush)res["RoseBrush"];
            ((TextBlock)row.Children[1]).Foreground = fg;
            var pill = (Border)row.Children[2];
            var count = vm.ChartCount((string)tile.Tag);
            pill.Visibility = count is > 0 ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)pill.Child).Text = count?.ToString() ?? "";
            ((TextBlock)pill.Child).Foreground = on ? fg : (Brush)res["MutedBrush"];
            pill.Background = on ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x38, 255, 255, 255)) : (Brush)res["BadgeBrush"];
        }
        ChartHeading.Text = vm.Tabs.FirstOrDefault(t => t.Id == vm.Chart)?.Label ?? "";
    }
    // The tabs fill the width; on a narrow window they keep a readable size and scroll sideways.
    private void OnChartTilesSize(object sender, SizeChangedEventArgs e) => ChartTiles.Width = Math.Max(e.NewSize.Width - 14, ChartTiles.ColumnDefinitions.Count * 196);

    // ---- Featured: the first game of the chosen list, with its wide art ----
    private CatalogCard? featured;
    private void PaintFeatured(GameListViewModel vm)
    {
        var card = vm.Searching ? null : vm.Items.FirstOrDefault();
        Featured.Visibility = card is null ? Visibility.Collapsed : Visibility.Visible;
        if (card is null || card == featured) return;
        featured = card;
        FeaturedWant.DataContext = card;
        FeaturedEyebrow.Text = (vm.Tabs.FirstOrDefault(t => t.Id == vm.Chart)?.Label ?? "").ToUpperInvariant() + " · " + vm.Text["GlFeatured"].ToUpperInvariant();
        FeaturedName.Text = card.Name;
        FeaturedMeta.Text = card.Meta;
        FeaturedArt.Opacity = 0;
        var art = new BitmapImage(new Uri(CatalogText.Hero(card.Item.AppId))) { DecodePixelWidth = 1600 };
        art.ImageOpened += (_, _) => HomeMotion.Fade(FeaturedArt, 1, 500);
        art.ImageFailed += (_, _) => { if (Uri.TryCreate(card.Wide, UriKind.Absolute, out var w)) FeaturedArt.Source = new BitmapImage(w) { DecodePixelWidth = 1600 }; FeaturedArt.Opacity = 1; };
        FeaturedArt.Source = art;
    }
    private async void OnFeaturedOpen(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || featured is null) return;
        ShowDetail(true);
        await vm.OpenAsync(featured.Item);
    }
    private void OnTogglePick(object sender, RoutedEventArgs e)
    {
        PickCard.Visibility = PickCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (PickCard.Visibility == Visibility.Visible) Motion.EnterPage(PickCard);
    }
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

    // Covers load in steps: the guessed portrait art → the exact portrait path from the store API (newer games keep
    // their art under hashed paths) → only then the wide header, shown whole at the top of the card (never stretched
    // into a blurry crop). The grid recycles cards, so each step checks the card still shows that game.
    private static readonly Dictionary<string, string> artCache = [];
    private static readonly HashSet<string> wideOnly = [];
    private void OnCoverContext(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not Image img) return;
        img.Opacity = 0; img.Source = null;
        if (args.NewValue is not CatalogCard card) return;
        var id = card.Item.AppId;
        if (wideOnly.Contains(id)) LoadWide(img, artCache.GetValueOrDefault(id) ?? card.Wide);
        else { Portrait(img); Load(img, artCache.GetValueOrDefault(id) ?? card.Cover); }
    }
    private void OnCoverOpened(object sender, RoutedEventArgs e) { if (sender is Image img) { img.Opacity = 1; } }
    private async void OnCoverFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not Image { DataContext: CatalogCard card } img || Vm is not { } vm) return;
        var id = card.Item.AppId;
        var tried = (img.Source as BitmapImage)?.UriSource?.ToString() ?? "";
        if (!wideOnly.Contains(id))
        {
            if (tried == card.Cover && await vm.PortraitAsync(id) is { } portrait && portrait != tried)
            {
                if (img.DataContext != card) return;
                artCache[id] = portrait; Load(img, portrait); return;
            }
            if (img.DataContext != card) return;
            wideOnly.Add(id); artCache.Remove(id);
            LoadWide(img, card.Wide); return;
        }
        if (await vm.ArtAsync(id) is { } art && art != tried && img.DataContext == card) { artCache[id] = art; LoadWide(img, art); }
        // Otherwise the name stays on the plum card underneath.
    }
    private static void Portrait(Image img) { img.Stretch = Stretch.UniformToFill; img.VerticalAlignment = VerticalAlignment.Stretch; }
    private static void LoadWide(Image img, string url) { img.Stretch = Stretch.Uniform; img.VerticalAlignment = VerticalAlignment.Top; Load(img, url); }
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
        var game = new Lotus.Core.Diary.DiaryEntry { Key = Lotus.Core.Diary.DiaryEntry.SteamKey(card.Item.AppId), AppId = card.Item.AppId, Name = card.Name, Cover = wideOnly.Contains(card.Item.AppId) ? card.Cover : artCache.GetValueOrDefault(card.Item.AppId) ?? card.Cover, Wide = card.Wide }; // the exact portrait when the guessed one failed
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
