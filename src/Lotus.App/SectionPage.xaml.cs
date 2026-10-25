using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using Windows.UI.ViewManagement;

namespace Lotus.App;

public sealed partial class SectionPage : Page
{
    private bool synchronizing;
    private ShellViewModel? shell;
    public bool ShowHome { get; private set; }
    public bool ShowOptimize { get; private set; }
    public bool ShowPerformance { get; private set; }
    public bool ShowGames { get; private set; }
    public bool ShowGameList { get; private set; }
    public bool ShowScore { get; private set; }
    public bool ShowTools { get; private set; }
    public bool ShowSettings { get; private set; }
    public bool ShowDiagnosis { get; private set; }
    public bool ShowRestore { get; private set; }
    public bool ShowLive { get; private set; }
    public bool ShowCompare { get; private set; }
    public bool ShowHistory { get; private set; }
    public bool ShowAppearance { get; private set; }
    public bool ShowMonitoringSettings { get; private set; }
    public bool ShowIntegrations { get; private set; }
    public bool ShowSafety { get; private set; }
    public bool ShowAdvanced { get; private set; }
    public bool ShowFaq { get; private set; }
    public SectionPage() => InitializeComponent();
    private void OnEditProfile(object sender, RoutedEventArgs e) => MainWindow.Shell?.OpenOnboarding(edit: true);
    // Home game shortcuts: the cover opens My Games (Play on the cover starts the game itself).
    private void OnOpenGames(object sender, RoutedEventArgs e) => shell?.NavigateCommand.Execute("games");
    private void OnHomeGameList(object sender, RoutedEventArgs e) { if (HomeGameListIcon is not null) Motion.Pop(HomeGameListIcon); shell?.NavigateCommand.Execute("gamelist"); }

    // ---- Home: "Continue playing" + live readiness (no repeats of the menu) ----
    private async void OnContinueLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SectionViewModel vm || ContinueCard is null) return;
        if (vm.Games is { } games)
        {
            // The library may still be scanning (startup): redraw when it fills in.
            var redraw = DispatcherQueue.CreateTimer(); redraw.Interval = TimeSpan.FromMilliseconds(400); redraw.IsRepeating = false;
            redraw.Tick += (_, _) => ContinueCard?.Show(vm);
            games.Games.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { redraw.Stop(); redraw.Start(); });
            await games.EnsureLoadedAsync();
        }
        ContinueCard?.Show(vm);
    }
    private void OnContinueDetails(object? sender, Lotus.Core.Games.GameEntry game)
    {
        if (DataContext is SectionViewModel { GameDetails: { } details }) details.Pending = game;
        shell?.NavigateCommand.Execute("games");
    }
    private void OnReadinessLoaded(object sender, RoutedEventArgs e) { if (DataContext is SectionViewModel vm) ReadinessCard?.Attach(vm); }

    // ---- Meus Jogos banner: big Steam games of the moment, mixed with the player's own games ----
    private async void OnGamesBannerLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SectionViewModel vm || GamesBanner is null) return;
        var t = vm.Text;
        GamesBanner.SetSlides([new(t["HbListEyebrow"], t["HbListTitle"], t["HbListBody"], null, 2, t["HbListAction"], () => shell?.NavigateCommand.Execute("gamelist"), "")]);
        var mine = new List<Controls.HomeBanner.Slide>();
        if (vm.Games is { } games)
        {
            await games.EnsureLoadedAsync();
            foreach (var g in games.InstalledGames().Where(g => g.Store == Lotus.Core.Games.GameStores.Steam).OrderByDescending(g => g.LastPlayed).Take(3))
                mine.Add(new(t["HbMineEyebrow"], g.Name, t["HbMineBody"], Lotus.Core.Catalog.CatalogText.Hero(g.Id), 0, t["GiPlay"], () => games.Play(g), ""));
        }
        var hot = new List<Controls.HomeBanner.Slide>();
        if (vm.GameList is { } list)
            foreach (var (chart, eyebrow, take) in new[] { ("topsellers", "HbHotEyebrow", 4), ("trending", "HbTrendEyebrow", 2) })
            {
                var c = await Task.Run(() => list.ChartForBannerAsync(chart));
                foreach (var item in (c?.Items ?? []).Where(i => !hot.Any(h => h.Title == i.Name)).Take(take))
                    hot.Add(new(t[eyebrow], item.Name, t["HbHotBody"], Lotus.Core.Catalog.CatalogText.Hero(item.AppId), hot.Count, t["GiDetails"],
                        () => { list.PendingOpen = item; shell?.NavigateCommand.Execute("gamelist"); }, ""));
            }
        if (GamesBanner is null || hot.Count + mine.Count == 0) return;
        // Steam highlights lead, the player's games in between.
        var all = new List<Controls.HomeBanner.Slide>(); var extra = new Queue<Controls.HomeBanner.Slide>(mine);
        foreach (var h in hot) { all.Add(h); if (all.Count % 2 == 0 && extra.Count > 0) all.Add(extra.Dequeue()); }
        all.AddRange(extra);
        GamesBanner.SetSlides(all);
    }
    // Meus Jogos opened from Home's "Details": go straight to that game's page.
    private void OnGamesListLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SectionViewModel { GameDetails: { Pending: { } game } details, Games: { } games })
        {
            details.Pending = null;
            var card = games.Games.FirstOrDefault(c => c.Game.Key == game.Key);
            if (card is not null) OnGameDetails(this, card);
        }
    }

    // Home's quick rescan (Meus Jogos keeps the full library tools).
    private async void OnHomeRescan(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SectionViewModel { Games: { } games } vm || HomeRescan is null) return;
        HomeRescan.IsEnabled = false; HomeRescanText.Text = vm.Text["HomeRescanning"];
        var spin = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(HomeRescanIcon);
        spin.CenterPoint = new System.Numerics.Vector3(6, 6, 0);
        var a = spin.Compositor.CreateScalarKeyFrameAnimation(); a.InsertKeyFrame(1, 360, spin.Compositor.CreateLinearEasingFunction()); a.Duration = TimeSpan.FromMilliseconds(900); a.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        if (Motion.Enabled) spin.StartAnimation("RotationAngleInDegrees", a);
        try { await games.RefreshAsync(); }
        finally
        {
            spin.StopAnimation("RotationAngleInDegrees"); spin.RotationAngleInDegrees = 0;
            HomeRescan.IsEnabled = true; HomeRescanText.Text = vm.Text.Format("HomeRescanDone", games.InstalledGames().Count);
            UiSound.Play(UiSound.Cue.Done);
        }
    }
    private void OnGuidedNavigate(object? sender, string id) => shell?.NavigateCommand.Execute(id);
    // Meus Jogos: a click on a game opens its page in place (data from the stores); Back returns to the library.
    private async void OnGameDetails(object? sender, Lotus.Core.Games.GameCard card)
    {
        if (DataContext is not SectionViewModel { GameDetails: { } details } || GamesList is null || GameDetail is null) return;
        GamesList.Visibility = Visibility.Collapsed; GameDetail.Visibility = Visibility.Visible; Motion.EnterPage(GameDetail);
        PageScroll.ChangeView(null, 0, null, true);
        await details.LoadInstalledAsync(card.Game);
    }
    private void OnGameDetailBack(object? sender, EventArgs e)
    {
        if (GamesList is null || GameDetail is null) return;
        GameDetail.Visibility = Visibility.Collapsed; GamesList.Visibility = Visibility.Visible; Motion.EnterPage(GamesList);
    }
    private void OnPlayGame(object? sender, Lotus.Core.Games.GameEntry game) { if (DataContext is SectionViewModel { Games: { } games }) games.Play(game); }
    private void OnHomeGameTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: not null } source && FindButton(source)) return; // the Play button handles its own tap
        shell?.NavigateCommand.Execute("games");
    }
    private static bool FindButton(DependencyObject d)
    {
        for (var x = d; x is not null; x = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(x)) if (x is Button) return true;
        return false;
    }

    // ---- Install Lotus on this PC (per user, no administrator) ----
    private readonly Lotus.Infrastructure.Setup.LotusSetup setup = new();
    private void OnSetupLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SectionViewModel vm) return;
        SetupStatus.Text = setup.IsInstalledCopy ? vm.Text.Format("SetupInstalledHere", setup.InstallDir)
            : setup.IsInstalled ? vm.Text.Format("SetupInstalledElsewhere", setup.InstallDir) : vm.Text["SetupNotInstalled"];
        SetupButton.Visibility = setup.IsInstalledCopy ? Visibility.Collapsed : Visibility.Visible;
        SetupButton.Content = vm.Text[setup.IsInstalled ? "SetupUpdate" : "SetupInstall"];
    }
    private async void OnInstallLotus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SectionViewModel vm) return;
        SetupButton.IsEnabled = false; SetupStatus.Text = vm.Text["SetupInstalling"];
        try
        {
            await Task.Run(() => setup.Install());
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ElementTheme.Dark, Title = vm.Text["SetupDoneTitle"], DefaultButton = ContentDialogButton.Primary,
                Content = RichText.Block(vm.Text.Format("SetupDoneBody", setup.InstallDir)), PrimaryButtonText = vm.Text["SetupOpenInstalled"], CloseButtonText = vm.Text["GsClose"]
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup.InstalledExe) { UseShellExecute = true })?.Dispose();
                Application.Current.Exit();
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Install failed"); SetupStatus.Text = vm.Text["SetupFailed"]; }
        finally { SetupButton.IsEnabled = true; OnSetupLoaded(sender, e); }
    }
    private void OnReplayWelcome(object sender, RoutedEventArgs e) => MainWindow.Shell?.OpenOnboarding(edit: false);
    private void OnRedoGuided(object sender, RoutedEventArgs e) { if (DataContext is SectionViewModel { Guided: { } g }) { g.Restart(); shell?.NavigateCommand.Execute("home"); } }
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not SectionViewModel vm) throw new ArgumentException("A section view model is required.");
        DataContext = vm; Language = vm.Text.Language; shell = vm.Shell;
        if (shell is not null) shell.PropertyChanged += OnRouteChanged;
        SyncRoute();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (shell is not null) shell.PropertyChanged -= OnRouteChanged;
        shell = null; base.OnNavigatedFrom(e);
    }
    private void OnRouteChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.CurrentRoute)) SyncRoute();
    }
    private void SyncRoute()
    {
        if (DataContext is not SectionViewModel vm || shell is null || vm.Section.Id != shell.SelectedSection.Id) return;
        synchronizing = true;
        try
        {
            ShowHome = vm.Section.Id == "home"; ShowOptimize = vm.Section.Id == "optimize";
            ShowPerformance = vm.Section.Id == "performance"; ShowGames = vm.Section.Id == "games"; ShowGameList = vm.Section.Id == "gamelist"; ShowScore = vm.Section.Id == "lotusscore"; ShowTools = vm.Section.Id == "tools"; ShowSettings = vm.Section.Id == "settings";
            ShowDiagnosis = ShowOptimize && shell.OptimizeTabIndex == 0;
            ShowRestore = ShowOptimize && shell.OptimizeTabIndex == 1;
            ShowLive = ShowPerformance && shell.PerformanceTabIndex == 0;
            ShowCompare = ShowPerformance && shell.PerformanceTabIndex == 1; ShowHistory = ShowPerformance && shell.PerformanceTabIndex == 2;
            ShowAppearance = ShowSettings && shell.SettingsCategoryIndex == 0;
            ShowMonitoringSettings = false;
            ShowIntegrations = ShowSettings && shell.SettingsCategoryIndex == 1;
            ShowSafety = ShowSettings && shell.SettingsCategoryIndex == 2; ShowAdvanced = ShowSettings && shell.SettingsCategoryIndex == 3;
            ShowFaq = ShowSettings && shell.SettingsCategoryIndex == 4;
            Bindings.Update();
            if (ShowOptimize) OptimizeTabs.SelectedItem = OptimizeTabs.Items[shell.OptimizeTabIndex];
            if (ShowPerformance) PerformanceTabs.SelectedItem = PerformanceTabs.Items[shell.PerformanceTabIndex];
            if (ShowSettings) SyncSettingsTabs(shell);
            ApplyMotion(vm);
        }
        finally { synchronizing = false; }
    }
    private void SyncSettingsTabs(ShellViewModel current)
    {
        var categories = current.SettingsCategories;
        if (SettingsTabs.Items.Count != categories.Count)
        {
            SettingsTabs.Items.Clear();
            foreach (var title in categories) SettingsTabs.Items.Add(new SelectorBarItem { Text = title });
        }
        var index = Math.Clamp(current.SettingsCategoryIndex, 0, SettingsTabs.Items.Count - 1);
        SettingsTabs.SelectedItem = SettingsTabs.Items[index];
    }
    // Staggered entrance and smooth reflow; disabled when the user or Windows asks for reduced motion.
    private void ApplyMotion(SectionViewModel vm)
    {
        var enabled = vm.MotionEnabled && new UISettings().AnimationsEnabled;
        foreach (var panel in new Panel?[] { PageContent, HomeContent, OptimizeContent, PerformanceContent, SettingsContent })
        {
            if (panel is null) continue;
            if (!enabled) { panel.ChildrenTransitions = null; continue; }
            if (panel.ChildrenTransitions is { Count: > 0 }) continue;
            panel.ChildrenTransitions = [new EntranceThemeTransition { IsStaggeringEnabled = true, FromVerticalOffset = 28 }, new RepositionThemeTransition()];
        }
    }
    private void OnOptimizeTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (synchronizing || shell is null || sender.SelectedItem is null) return;
        var index = sender.Items.IndexOf(sender.SelectedItem);
        if (index is >= 0 and <= 1) shell.NavigateCommand.Execute(new[] { "analysis", "restore" }[index]);
    }
    private void OnPerformanceTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (synchronizing || shell is null || sender.SelectedItem is null) return;
        var index = sender.Items.IndexOf(sender.SelectedItem);
        if (index is >= 0 and <= 2) shell.NavigateCommand.Execute(new[] { "monitoring", "benchmark", "history" }[index]);
    }
    private void OnSettingsTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (synchronizing || shell is null || sender.SelectedItem is null) return;
        var index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0) shell.SettingsCategoryIndex = index;
    }
    // Runs the read-only hardware check once, the first time Home is shown.
    private async void OnHardwareCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Lotus.Core.Diagnostics.HardwareDiagnosticsViewModel { HasFindings: false, IsBusy: false } hw }) await hw.CheckAsync();
    }
    // Opens only Windows Settings pages (ms-settings:); nothing is changed by Lotus.
    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string uri } && uri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var inset = Math.Max(0, (e.NewSize.Width - 1180) / 2); PageContent.Margin = new Thickness(inset, 0, inset, 0);
    }
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PageContent.Padding = e.NewSize.Width < 700 ? new Thickness(16, 20, 16, 32) : new Thickness(32, 28, 32, 40);
        // Home: continue card + side column side by side; stacked below ~980 px.
        if (HomeTop is not null && HomeSide is not null && HomeSideColumn is not null)
        {
            var narrow = e.NewSize.Width < 980;
            HomeSideColumn.Width = narrow ? new GridLength(0) : new GridLength(360);
            Grid.SetColumn(HomeSide, narrow ? 0 : 1); Grid.SetRow(HomeSide, narrow ? 1 : 0);
            HomeTop.ColumnSpacing = narrow ? 0 : 20;
        }
    }
}
