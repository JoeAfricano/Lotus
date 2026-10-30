using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// "Meu diário": the player's profile, progress, badges and every logged game. A click on a game edits it.
public sealed partial class DiaryPanel : UserControl
{
    public event EventHandler? GameListRequested;
    private DiaryViewModel? Vm => DataContext as DiaryViewModel;
    public DiaryPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
        DataContextChanged += (_, _) => { if (Vm is { } vm) { vm.PropertyChanged -= OnVm; vm.PropertyChanged += OnVm; Render(); } };
        Unloaded += (_, _) => { if (Vm is { } vm) vm.PropertyChanged -= OnVm; };
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiaryViewModel.LevelPercent) or nameof(DiaryViewModel.Progress)) DispatcherQueue.TryEnqueue(Render);
        if (e.PropertyName is nameof(DiaryViewModel.Section)) DispatcherQueue.TryEnqueue(() => ShowSection(animate: true));
        if (e.PropertyName is nameof(DiaryViewModel.GenreOptions) or nameof(DiaryViewModel.ActiveFilters) or nameof(DiaryViewModel.Tab)) DispatcherQueue.TryEnqueue(BuildChips);
        if (e.PropertyName is nameof(DiaryViewModel.ViewMode)) DispatcherQueue.TryEnqueue(PaintView);
    }

    // Games, achievements or Steam. The bar follows the view model (a toast can open "Conquistas" directly).
    private bool showing;
    private void ShowSection(bool animate)
    {
        if (Vm is not { } vm) return;
        showing = true;
        SectionBar.SelectedItem = vm.Section switch { "stats" => SectionStats, "trophies" => SectionTrophies, "steam" => SectionSteam, _ => SectionGames };
        showing = false;
        var target = vm.Section switch { "stats" => (FrameworkElement)StatsArea, "trophies" => TrophiesArea, "steam" => SteamArea, _ => GamesArea };
        foreach (var area in new FrameworkElement[] { GamesArea, StatsArea, TrophiesArea, SteamArea })
            area.Visibility = area == target ? Visibility.Visible : Visibility.Collapsed;
        if (animate) Motion.EnterPage(target);
        if (target == TrophiesArea) TrophiesArea.Celebrate();
        // Opened on one game (from a game page): bring that game into view.
        if (target == SteamArea && vm.Achievements?.HasSelected == true)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => SteamArea.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = Motion.Active, VerticalAlignmentRatio = 0 }));
    }
    private void OnRecap(object? sender, int year) => MainWindow.Shell?.OpenDiaryRecap(year);
    private void OnSection(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (showing || Vm is not { } vm || sender.SelectedItem?.Tag is not string id || id == vm.Section) return;
        UiSound.Play(UiSound.Cue.Toggle);
        vm.Section = id;
    }

    private void Render()
    {
        if (Vm is not { } vm) return;
        FullInitial.Text = Initial.Text = vm.Name.Length > 0 ? char.ToUpperInvariant(vm.Name[0]).ToString() : "P";
        Picture.Visibility = FullPicture.Visibility = Visibility.Collapsed;
        if (vm.Avatar is { Length: > 0 } a && File.Exists(a))
        {
            FullPictureBrush.ImageSource = PictureBrush.ImageSource = new BitmapImage(new Uri(a)) { DecodePixelWidth = 200 };
            Picture.Visibility = FullPicture.Visibility = Visibility.Visible;
        }
        PaintHeader();
        // XP bar glides to its value.
        var target = vm.LevelPercent; var from = XpBar.Value;
        Motion.Tween(XpBar, TimeSpan.FromMilliseconds(800), p => XpBar.Value = FullXpBar.Value = from + (target - from) * p, Motion.EaseOutQuart);
        PaintTabs();
        BuildChips(); PaintView();
        _ = vm.Deals?.RefreshAsync();
        ShowSection(animate: false);
    }

    // Selected tab: gold underline and full text; others muted.
    private void PaintTabs()
    {
        if (Vm is not { } vm) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            for (var i = 0; i < TabBar.Items.Count; i++)
                if (TabBar.ContainerFromIndex(i) is ContentPresenter { Content: DiaryTab tab } cp && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(cp) > 0 && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(cp, 0) is Button b)
                {
                    var on = tab.Id == vm.Tab;
                    b.BorderBrush = on ? (Brush)Application.Current.Resources["GoldGradientBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    b.Opacity = on ? 1 : 0.65;
                }
        });
    }

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { Tag: string id } f) return;
        vm.Tab = id; Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle); PaintTabs();
    }
    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (Vm is { } vm && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) vm.Query = sender.Text; }
    private void OnEntry(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: DiaryCard card }) MainWindow.Shell?.OpenDiaryEditor(card.Entry); }

    // ---- Layout switch ----
    private void OnView(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { Tag: string mode } f || mode == vm.ViewMode) return;
        vm.ViewMode = mode; UiSound.Play(UiSound.Cue.Toggle); Motion.Pop(f);
    }
    // The active layout button is filled with the accent; the others stay plain.
    private void PaintView()
    {
        if (Vm is not { } vm) return;
        foreach (var b in new[] { ViewGrid, ViewList, ViewTable })
        {
            var on = (string)b.Tag == vm.ViewMode;
            b.Background = on ? (Brush)Application.Current.Resources["AccentSoftBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            b.Foreground = (Brush)Application.Current.Resources[on ? "RoseBrush" : "MutedBrush"];
        }
    }
    private async void OnDeal(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: DealRow d } && Uri.TryCreate(d.StoreUrl, UriKind.Absolute, out var u)) await Windows.System.Launcher.LaunchUriAsync(u); }
    private void OnGridItem(object sender, ItemClickEventArgs e) { if (e.ClickedItem is DiaryCard card) MainWindow.Shell?.OpenDiaryEditor(card.Entry); }
    // Table headers sort by that column (a second click on the same column goes back to "recent").
    private void OnColumnSort(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { Tag: string sort }) return;
        vm.Sort = vm.Sort == sort ? "recent" : sort;
    }
    private void OnClearFilters(object sender, RoutedEventArgs e) { Vm?.ClearFilters(); UiSound.Play(UiSound.Cue.Toggle); }

    // ---- Filter chips: one dropdown per dimension, built from the player's own games ----
    private void BuildChips()
    {
        if (Vm is not { } vm) return;
        FilterChips.Children.Clear();
        Chip(vm.Text["DyFilterGenre"], vm.GenreOptions, vm.Genre, v => vm.Genre = v, "genre");
        Chip(vm.Text["DyFilterPlatform"], vm.PlatformOptions, vm.Platform, v => vm.Platform = v, "platform");
        Chip(vm.Text["DyFilterStore"], vm.StoreOptions, vm.Store, v => vm.Store = v, "store");
        Chip(vm.Text["DyFilterRating"], vm.RatingOptions, vm.RatingBand, v => vm.RatingBand = v, "rating");
        Chip(vm.Text["DyFilterYear"], vm.YearOptions, vm.Year, v => vm.Year = v, "year");
        SavedChip(vm);

        void Chip(string title, IReadOnlyList<DiaryTab> options, string? current, Action<string?> set, string id)
        {
            if (options.Count == 0) return;
            var label = current is null ? title : $"{title}: {options.FirstOrDefault(o => o.Id == current)?.Label ?? current}";
            var menu = new MenuFlyout();
            var any = new RadioMenuFlyoutItem { Text = vm.Text["DyFilterAny"], GroupName = id, IsChecked = current is null };
            any.Click += (_, _) => set(null); menu.Items.Add(any);
            foreach (var o in options)
            {
                var item = new RadioMenuFlyoutItem { Text = $"{o.Label}  ({o.Count})", GroupName = id, IsChecked = o.Id == current };
                item.Click += (_, _) => set(o.Id); menu.Items.Add(item);
            }
            FilterChips.Children.Add(ChipButton(label, current is not null, menu, "diary-filter-" + id));
        }
    }
    private void SavedChip(DiaryViewModel vm)
    {
        var menu = new MenuFlyout();
        foreach (var f in vm.SavedFilters)
        {
            var sub = new MenuFlyoutSubItem { Text = f.Name };
            var apply = new MenuFlyoutItem { Text = f.Name, Icon = new FontIcon { Glyph = "\uE71C" } }; apply.Click += (_, _) => vm.ApplyFilter(f);
            var delete = new MenuFlyoutItem { Text = vm.Text["DyDeleteFilter"], Icon = new FontIcon { Glyph = "\uE74D" } }; delete.Click += (_, _) => vm.DeleteFilter(f.Name);
            sub.Items.Add(apply); sub.Items.Add(delete); menu.Items.Add(sub);
        }
        if (vm.SavedFilters.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = vm.Text["DySavedNone"], IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        var save = new MenuFlyoutItem { Text = vm.Text["DySaveFilter"], Icon = new FontIcon { Glyph = "\uE74E" }, IsEnabled = vm.HasFilters || vm.Tab != "all" };
        save.Click += async (_, _) => await AskFilterName(vm);
        menu.Items.Add(save);
        FilterChips.Children.Add(ChipButton("★ " + vm.Text["DySaved"], false, menu, "diary-filter-saved"));
    }
    private Button ChipButton(string label, bool active, MenuFlyout menu, string id)
    {
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = label, FontSize = 12.5, FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal }, new FontIcon { Glyph = "\uE70D", FontSize = 9 } } },
            Padding = new Thickness(12, 5, 10, 6), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Flyout = menu,
            Background = (Brush)Application.Current.Resources[active ? "AccentSoftBrush" : "CardBrush"],
            BorderBrush = (Brush)Application.Current.Resources[active ? "RoseBrush" : "CardBorderBrush"],
            Foreground = (Brush)Application.Current.Resources[active ? "RoseBrush" : "MainTextBrush"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, id);
        return b;
    }
    private async Task AskFilterName(DiaryViewModel vm)
    {
        var box = new TextBox { PlaceholderText = vm.Text["DySaveFilterName"], MaxLength = 40, MinWidth = 320 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = vm.Text["DySaveFilterTitle"], Content = box, PrimaryButtonText = vm.Text["DySave"], CloseButtonText = vm.Text["DlgCancel"], DefaultButton = ContentDialogButton.Primary };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0; dialog.IsPrimaryButtonEnabled = false;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { vm.SaveCurrentFilter(box.Text); UiSound.Play(UiSound.Cue.Done); BuildChips(); }
    }

    // ---- "Top 4" showcase: an empty slot lists the diary's games; a filled one opens, swaps or clears ----
    private void OnShowcaseSlot(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: ShowcaseSlot slot } anchor) return;
        var menu = new MenuFlyout();
        MenuFlyoutItemBase Pick(DiaryEntry entry)
        {
            var item = new MenuFlyoutItem { Text = entry.Rating is { } r ? $"{entry.Name}  ·  {r:0.#}" : entry.Name };
            item.Click += (_, _) => { vm.SetShowcase(slot.Index, entry.Key); UiSound.Play(UiSound.Cue.Done); };
            return item;
        }
        var candidates = vm.ShowcaseCandidates().Where(c => c.Key != slot.Card?.Key).Take(20).ToList();
        if (slot.Card is { } card)
        {
            var open = new MenuFlyoutItem { Text = vm.Text["DyShowcaseOpen"], Icon = new FontIcon { Glyph = "\uE70F" } }; open.Click += (_, _) => MainWindow.Shell?.OpenDiaryEditor(card.Entry);
            var swap = new MenuFlyoutSubItem { Text = vm.Text["DyShowcaseSwap"], Icon = new FontIcon { Glyph = "\uE8AB" } };
            foreach (var c in candidates) swap.Items.Add(Pick(c));
            var remove = new MenuFlyoutItem { Text = vm.Text["DyShowcaseRemove"], Icon = new FontIcon { Glyph = "\uE711" } }; remove.Click += (_, _) => vm.SetShowcase(slot.Index, null);
            menu.Items.Add(open); if (candidates.Count > 0) menu.Items.Add(swap); menu.Items.Add(remove);
        }
        else if (candidates.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = vm.Text["DyShowcaseEmpty"], IsEnabled = false });
        else foreach (var c in candidates) menu.Items.Add(Pick(c));
        menu.ShowAt(anchor);
    }

    // ---- Profile header: compact band or the full layout ----
    private void OnHeaderToggle(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        vm.HeaderCompact = !vm.HeaderCompact; UiSound.Play(UiSound.Cue.Toggle);
        PaintHeader(); Motion.Settle(vm.HeaderCompact ? HeaderCompact : HeaderFull);
    }
    private void PaintHeader()
    {
        if (Vm is not { } vm) return;
        HeaderCompact.Visibility = vm.HeaderCompact ? Visibility.Visible : Visibility.Collapsed;
        HeaderFull.Visibility = vm.HeaderCompact ? Visibility.Collapsed : Visibility.Visible;
        HeaderToggleIcon.Glyph = vm.HeaderCompact ? "\uE740" : "\uE73F"; // expand / collapse
        var tip = vm.Text[vm.HeaderCompact ? "DyHeaderExpand" : "DyHeaderCompact"];
        ToolTipService.SetToolTip(HeaderToggle, tip); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HeaderToggle, tip);
    }
    private void OnOpenGameList(object sender, RoutedEventArgs e) => GameListRequested?.Invoke(this, EventArgs.Empty);
}
