using Lotus.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Globalization;
using Lotus.Core.Telemetry;

namespace Lotus.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }
    private readonly ITextCatalog text;
    private readonly PreferencesService preferences;
    private readonly PreferencesViewModel preferencesViewModel;
    private readonly ThemePalette palette;
    private readonly Lotus.Core.Profile.OnboardingViewModel onboarding;
    public static MainWindow? Shell { get; private set; }
    private bool initialized;
    private bool updatingLayout;
    private bool? narrow;
    private string? displayedSectionId;
    private bool updatingNavigation;
    private bool windowActive;
    private readonly ExperienceViewModel experience;
    private readonly Lotus.Core.Benchmark.BenchmarkViewModel benchmark;
    private readonly Lotus.Core.Optimization.OptimizationViewModel optimizer;
    private readonly Lotus.Core.Optimization.PowerProfilesViewModel powerProfiles;
    private readonly Lotus.Core.Diagnostics.HardwareDiagnosticsViewModel hardware;
    private readonly Lotus.Core.Games.GamesViewModel games;
    private readonly Lotus.Core.Integrations.IntegrationsViewModel integrations;
    private readonly Lotus.Core.Score.LotusScoreViewModel score;
    private readonly Lotus.Core.Tools.ToolsViewModel tools;
    private GuidedSetupViewModel? guided;
    private Lotus.Core.Catalog.GameListViewModel? gameList;
    private Lotus.Core.Games.PlayerPanelViewModel? player;
    private Lotus.Core.Diary.GameDiary diary = null!;
    private Lotus.Core.Diary.DiaryViewModel diaryPage = null!;
    private Lotus.Core.Diary.DiaryEditorViewModel diaryEditor = null!;
    private Lotus.Core.Catalog.GameDetailsViewModel? gameDetails;
    private BrandPulse? titlePulse;
    private BrandPulse? sidebarPulse;
    private BrandPulse? flowerPulse;
    private BrandPulse? compactFlowerPulse;
    private readonly MonitoringViewModel monitoring;
    private readonly Lotus.Core.Diagnostics.MemoryAnalysisViewModel memoryAnalysis;
    private bool? brandAnimated;
    private readonly bool brandProofRequested = Environment.GetCommandLineArgs().Contains("--brand-motion-proof");
    private BrandMotionProof? brandProof;
    private bool brandProofShown;

    public MainWindow(ShellViewModel viewModel, ITextCatalog text, PreferencesService preferences, PreferencesViewModel preferencesViewModel, ThemePalette palette, MonitoringViewModel monitoring, Lotus.Core.Diagnostics.MemoryAnalysisViewModel memoryAnalysis, ExperienceViewModel experience, Lotus.Core.Benchmark.BenchmarkViewModel benchmark, Lotus.Core.Optimization.OptimizationViewModel optimizer, Lotus.Core.Optimization.PowerProfilesViewModel powerProfiles, Lotus.Core.Diagnostics.HardwareDiagnosticsViewModel hardware, Lotus.Core.Games.GamesViewModel games, Lotus.Core.Integrations.IntegrationsViewModel integrations, Lotus.Core.Score.LotusScoreViewModel score, Lotus.Core.Tools.ToolsViewModel tools)
    {
        ViewModel = viewModel;
        this.text = text;
        this.preferences = preferences;
        this.preferencesViewModel = preferencesViewModel;
        this.palette = palette;
        this.monitoring = monitoring;
        this.memoryAnalysis = memoryAnalysis;
        this.experience = experience; this.benchmark = benchmark; this.optimizer = optimizer; this.powerProfiles = powerProfiles; this.hardware = hardware; this.games = games; this.integrations = integrations; this.score = score; this.tools = tools;
        guided = new GuidedSetupViewModel(preferences, text, ex => Serilog.Log.Warning(ex, "Guided setup"), powerProfiles, tools, integrations);
        var journal = new Lotus.Core.Games.PlayerJournal(new Lotus.Infrastructure.Games.JsonPlayerJournalStore(), ex => Serilog.Log.Warning(ex, "Player journal"));
        games.IsFavorite = journal.IsFavorite;
        journal.Changed += (_, _) => games.RefreshFeatured();
        player = new Lotus.Core.Games.PlayerPanelViewModel(games, journal, text, () => Lotus.Infrastructure.Games.SteamPlaytime.Read());
        diary = new Lotus.Core.Diary.GameDiary(new Lotus.Infrastructure.Games.JsonGameDiaryStore(), ex => Serilog.Log.Warning(ex, "Game diary"));
        diaryPage = new Lotus.Core.Diary.DiaryViewModel(diary, text, preferences);
        diaryEditor = new Lotus.Core.Diary.DiaryEditorViewModel(diary, text);
        diary.Changed += (_, _) => DispatcherQueue.TryEnqueue(ShowDiaryLevel);
        var catalog = new Lotus.Infrastructure.Catalog.SteamCatalog(language: preferences.Current.Language);
        gameList = new Lotus.Core.Catalog.GameListViewModel(catalog, text, ex => Serilog.Log.Warning(ex, "Game list"), games.InstalledGames);
        gameDetails = new Lotus.Core.Catalog.GameDetailsViewModel(catalog, text, ex => Serilog.Log.Warning(ex, "Game details"));
        // Preparar e jogar: same Game profile and journal as Otimizar; the game is followed until it closes.
        games.GameProfile = new Lotus.Core.Optimization.GameProfileAdapter(powerProfiles);
        games.Watch = new Lotus.Core.Games.PollingGameWatch(new Lotus.Infrastructure.Games.WindowsGameProcessCheck());
        games.Memory = new Lotus.Infrastructure.Games.WindowsMemoryPressure();
        games.Goal = () => preferences.Current.Goal;
        _ = games.EnsureLoadedAsync(); // Home shows game shortcuts right away
        SetUpBackground();
        try { experience.QuickFacts = Lotus.Infrastructure.Tools.QuickHardwareFacts.Read(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Quick hardware facts"); }
        games.SessionEnded += OnGameSessionEnded;
        InitializeComponent();
        Shell = this;
        ShellRoot.DataContext = ViewModel;
        ShellRoot.Language = text.Language;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Lotus.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 150, 130, 142);
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x1F, 255, 255, 255);
        AppWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 245, 239, 243);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        foreach (var section in ViewModel.Items.Where(s => s.Id is not ("gamelist" or "diary"))) // Game list and the diary open from Home, the title bar and the profile
        {
            var item = new NavigationViewItem
            {
                Content = section.Id == "settings" ? null : NavLabel(section), Tag = section.Id, // settings: gear only, so every section fits
                Icon = new FontIcon { Glyph = section.Glyph, FontSize = 17 },
                MinHeight = 44, Margin = new Thickness(1, 2, 1, 2), CornerRadius = new CornerRadius(10), HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(item, section.Title);
            AutomationProperties.SetAutomationId(item, "nav-" + section.Id);
            ToolTipService.SetToolTip(item, section.Title);
            Navigation.MenuItems.Add(item);
        }
        Navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => OnPaneStateChanged());
        initialized = true;
        UpdatePaneChrome();
        if (preferences.LoadStatus is not ("Loaded" or "Missing")) ShowWarning(text["LoadWarning"]);
        ViewModel.PropertyChanged += OnRouteChanged;
        experience.DemandChanged += OnExperienceDemandChanged;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        // Player profile: accent color, corner badge and the first-run welcome.
        onboarding = new Lotus.Core.Profile.OnboardingViewModel(preferences, new Lotus.Infrastructure.Profile.LocalProfileSource(), text, ex => Serilog.Log.Warning(ex, "Profile"),
            new Lotus.Core.Profile.RecommendedAppsViewModel(new Lotus.Infrastructure.Profile.WingetAppInstaller(), text, ex => Serilog.Log.Warning(ex, "Recommended apps")));
        onboarding.AccentPreview += (_, accent) => palette.ApplyAccent(accent);
        Onboarding.Closed += (_, _) => { palette.ApplyAccent(preferences.Current.Accent); ShowProfile(); ShowRoute(force: true); };
        palette.ApplyAccent(preferences.Current.Accent);
        UiSound.Volume = preferences.Current.UiSoundVolume / 100.0; UiSound.Enabled = preferences.Current.UiSounds;
        ShowProfile();
        if (onboarding.NeedsWelcome) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => OpenOnboarding(edit: false));
        else DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (AppWindow.IsVisible) UiSound.Play(UiSound.Cue.Startup); }); // soft hello (not when starting in the tray)
        ShowRoute();
        preferences.PropertyChanged += OnPreferencesChanged;
        Closed += (_, _) =>
        {
            initialized = false;
            memoryAnalysis.SetActive(false);
            monitoring.SetActive(false);
            StopBrandMotion();
            preferences.PropertyChanged -= OnPreferencesChanged;
            ViewModel.PropertyChanged -= OnRouteChanged;
            experience.DemandChanged -= OnExperienceDemandChanged;
        };
        Activated += (_, args) =>
        {
            windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
            UpdateAmbientState();
            // Deactivation alone does not interrupt an explicit analysis.
            UpdateBrandMotion();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateMonitoringDemand);
        };
        AppWindow.Changed += (_, _) => { UpdateMonitoringDemand(); UpdateBrandMotion(); UpdateAmbientState(); };
        TitleLogo.SizeChanged += (_, _) => UpdateBrandMotion();
        BrandLogo.SizeChanged += (_, _) => UpdateBrandMotion();
        BrandFlower.SizeChanged += (_, _) => UpdateBrandMotion();
        CompactBrandFlower.SizeChanged += (_, _) => UpdateBrandMotion();
        ShellRoot.Loaded += (_, _) =>
        {
            UpdateNavigationMode(ShellRoot.ActualWidth);
            UpdateMinimumSize();
            UpdatePaneChrome();
            UpdateAmbientState();
            UpdateBrandMotion();
            ShellRoot.XamlRoot.Changed += (_, _) => UpdateMinimumSize();
            if (brandProofRequested && !brandProofShown)
            {
                brandProofShown = true;
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        brandProof = new(ShellRoot.XamlRoot, BrandFlower,
                            () => initialized && AppWindow.IsVisible && !preferences.Current.ReduceMotion &&
                                AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized } &&
                                !new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast, text);
                        await brandProof.ShowAsync();
                    }
                    catch (Exception ex) { Serilog.Log.Error(ex, "Brand scale proof failed"); }
                    finally { brandProof?.Dispose(); brandProof = null; }
                });
            }
        };
    }
    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PreferencesService.Current)) return;
        palette.Apply(preferences.Current.Theme);
        palette.ApplyAccent(preferences.Current.Accent);
        Motion.Enabled = !preferences.Current.ReduceMotion;
        UiSound.Volume = preferences.Current.UiSoundVolume / 100.0; UiSound.Enabled = preferences.Current.UiSounds;
        ApplyStartup();
        UpdateAmbientState();
        UpdateBrandMotion();
        if (text.Language == preferences.Current.Language) return;
        text.SetLanguage(preferences.Current.Language);
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(text.Language);
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(text.Language);
        ShellRoot.Language = text.Language;
        ViewModel.RefreshLanguage();
        monitoring.RefreshLanguage();
        experience.RefreshLanguage(); benchmark.RefreshLanguage(); optimizer.RefreshLanguage(); powerProfiles.RefreshLanguage(); hardware.RefreshLanguage(); games.RefreshLanguage(); integrations.RefreshLanguage(); score.RefreshLanguage(); tools.RefreshLanguage();
        foreach (NavigationViewItem item in Navigation.MenuItems)
        {
            var section = ViewModel.Items.Single(s => s.Id == (string)item.Tag);
            item.Content = section.Id == "settings" ? null : NavLabel(section);
            AutomationProperties.SetName(item, section.Title);
            ToolTipService.SetToolTip(item, section.Title);
        }
        if (ShellRoot.IsLoaded) UpdateNavigationMode(ShellRoot.ActualWidth); // keep icon-only menu when narrow
        UpdatePaneChrome();
        // Replace only the current visual page, retaining the same destination and preferences VM.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ShowRoute(force: true);
        });
    }
    private void UpdateMinimumSize()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var scale = ShellRoot.XamlRoot.RasterizationScale;
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(600 * scale);
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(480 * scale);
        }
    }
    private void OnShellSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Restore compact state only after NavigationView has created its template.
        // Closing the pane during construction can leave menu items without bounds.
        if (initialized && ShellRoot.IsLoaded) UpdateNavigationMode(e.NewSize.Width);
    }
    private void UpdateNavigationMode(double width)
    {
        // Top navigation at every width; NavigationView moves overflow items into its "More" menu by itself.
        // Below ~1180 px the labels go away (icons + tooltips) so every section stays visible, none in "More".
        var compact = width < 1180;
        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
            if (item.Tag is string id && ViewModel.Items.FirstOrDefault(s => s.Id == id) is { } section)
                item.Content = compact || id == "settings" ? null : NavLabel(section);
        var nowNarrow = width < 1050;
        if (narrow == nowNarrow) return;
        updatingLayout = true;
        narrow = nowNarrow;
        Navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Top;
        updatingLayout = false;
        UpdatePaneChrome();
    }
    private void OnPaneStateChanged()
    {
        UpdatePaneChrome();
        // The sidebar preference only applies to left navigation; top navigation never overwrites it.
        if (Navigation.PaneDisplayMode == NavigationViewPaneDisplayMode.Top) return;
        if (!initialized || updatingLayout || narrow != false) return;
        if (!preferences.Update(preferences.Current with { SidebarExpanded = Navigation.IsPaneOpen }))
            ShowWarning(text["SidebarSaveFailed"]);
    }
    private void UpdatePaneChrome()
    {
        var top = Navigation.PaneDisplayMode == NavigationViewPaneDisplayMode.Top;
        var open = top || Navigation.IsPaneOpen;
        // In top mode the brand lives in the title bar; the profile is a compact chip on the right.
        BrandHeader.Visibility = open && !top ? Visibility.Visible : Visibility.Collapsed;
        ExpandedFooter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        CompactFooter.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        DiaryButton.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ShowDiaryLevel();
        UpdateToggleAccessibility(Navigation, text[open ? "CloseNavigation" : "OpenNavigation"]);
        UpdateBrandMotion();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateBrandMotion);
    }
    private void UpdateAmbientState()
    {
        if (!initialized) return;
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        AmbientBackground.SetWindowState(windowActive, visible, preferences.Current.ReduceMotion);
    }
    private void UpdateBrandMotion()
    {
        if (!initialized || ShellRoot.XamlRoot is null) return;
        var systemAnimations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        var animate = !brandProofRequested && AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized }
            && !preferences.Current.ReduceMotion;
        if (brandAnimated != animate)
        {
            StopBrandMotion(); brandAnimated = animate;
            Serilog.Log.Information("Brand motion policy {Animate} WindowsAnimations {WindowsAnimations} ReduceMotion {ReduceMotion} Proof {Proof}",
                animate, systemAnimations, preferences.Current.ReduceMotion, brandProofRequested);
        }
        brandProof?.Refresh();
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) { StopBrandMotion(); return; }
        titlePulse ??= BrandPulse.TryCreate(TitleLogo, animate);
        if (Navigation.IsPaneOpen)
        {
            sidebarPulse ??= BrandPulse.TryCreate(BrandLogo, animate);
            flowerPulse ??= BrandPulse.TryCreate(BrandFlower, animate, flower: true);
            compactFlowerPulse?.Dispose(); compactFlowerPulse = null;
        }
        else
        {
            sidebarPulse?.Dispose(); sidebarPulse = null; flowerPulse?.Dispose(); flowerPulse = null;
            compactFlowerPulse ??= BrandPulse.TryCreate(CompactBrandFlower, animate, flower: true);
        }
    }
    private void StopBrandMotion()
    {
        titlePulse?.Dispose(); titlePulse = null;
        sidebarPulse?.Dispose(); sidebarPulse = null;
        flowerPulse?.Dispose(); flowerPulse = null;
        compactFlowerPulse?.Dispose(); compactFlowerPulse = null;
    }
    private static void UpdateToggleAccessibility(DependencyObject parent, string label)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button { Name: "TogglePaneButton" } button)
            {
                AutomationProperties.SetName(button, label);
                ToolTipService.SetToolTip(button, label);
                return;
            }
            UpdateToggleAccessibility(child, label);
        }
    }
    private void ShowWarning(string message)
    {
        PreferenceWarning.Message = message;
        PreferenceWarning.IsOpen = true;
    }
    private void OnGameListButton(object sender, RoutedEventArgs e) { Motion.Pop(GameListIcon); UiSound.Play(UiSound.Cue.Navigate); ViewModel.NavigateCommand.Execute("gamelist"); }
    // The drag area stops where the Game list button begins.
    private void OnGameListButtonSize(object sender, SizeChangedEventArgs e) => DragRegion.Margin = new Thickness(0, 0, e.NewSize.Width + 160, 0);
    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (updatingNavigation || args.SelectedItem is not NavigationViewItem { Tag: string id } item) return;
        if (item.Icon is { } icon) Motion.Pop(icon); // the chosen section's icon springs
        UiSound.Play(UiSound.Cue.Navigate);
        ViewModel.NavigateCommand.Execute(id);
        if (narrow == true && Navigation.PaneDisplayMode != NavigationViewPaneDisplayMode.Top) Navigation.IsPaneOpen = false;
    }
    private void OnRouteChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.CurrentRoute)) ShowRoute();
    }
    private void ShowRoute(bool force = false)
    {
        var group = ViewModel.SelectedSection.Id;
        updatingNavigation = true;
        Navigation.SelectedItem = Navigation.MenuItems.Cast<NavigationViewItem>().FirstOrDefault(item => (string)item.Tag == group);
        if (Navigation.SelectedItem is null) foreach (var i in Navigation.MenuItems.OfType<NavigationViewItem>()) i.IsSelected = false; // Game list: no menu item lit
        // The control leaves its underline on the last item when nothing is selected: hide it on the Game list.
        if (Application.Current.Resources["NavigationViewSelectionIndicatorForeground"] is SolidColorBrush indicator)
            indicator.Color = Navigation.SelectedItem is null ? Microsoft.UI.Colors.Transparent : Windows.UI.Color.FromArgb(255, 243, 201, 174);
        updatingNavigation = false;
        if (force || displayedSectionId != group)
        {
            ContentFrame.Navigate(typeof(SectionPage), new SectionViewModel(ViewModel.SelectedSection, text, preferencesViewModel,
                !preferences.Current.ReduceMotion, monitoring, memoryAnalysis, ViewModel, experience, benchmark, optimizer, powerProfiles, hardware, games, integrations, score, tools) { Guided = guided, GameList = gameList, GameDetails = gameDetails, Diary = diaryPage }, new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear(); displayedSectionId = group;
            Motion.EnterPage(ContentFrame); // new section rises and fades in
        }
        UpdateMonitoringDemand();
    }
    private void OnExperienceDemandChanged(object? sender, EventArgs args) => UpdateMonitoringDemand();

    // LOTUS SCORE stands out in the menu: bold, gold, always upper case.
    private static object NavLabel(Section section) => section.Id != "lotusscore" ? section.Title
        : new TextBlock { Text = section.Title.ToUpperInvariant(), FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["GoldGradientBrush"], CharacterSpacing = 40 };

    // ---- Background mode: tray icon, close-to-tray, start with Windows, in-game hotkey ----
    private TrayIcon? tray;
    private bool exiting, toldAboutTray;
    private readonly Lotus.Infrastructure.Games.RtssOsd osd = new();
    private Lotus.Core.Games.GamesViewModel.StutterReport? pendingReport;
    private Lotus.Core.Games.GameEntry? pendingGame;
    private bool analysing;

    private void SetUpBackground()
    {
        tray = new TrayIcon("Lotus", text["TrayOpen"], text["TrayExit"]);
        tray.Open += (_, _) => DispatcherQueue.TryEnqueue(ShowFromTray);
        tray.Exit += (_, _) => DispatcherQueue.TryEnqueue(() => { exiting = true; Close(); });
        tray.Hotkey += (_, _) => DispatcherQueue.TryEnqueue(() => _ = AnalyseRunningGameAsync());
        tray.Show();
        AppWindow.Closing += (_, args) =>
        {
            if (exiting || !preferences.Current.CloseToTray) return;
            args.Cancel = true; HideToTray(firstTime: !toldAboutTray);
        };
        Closed += (_, _) => { tray?.Dispose(); osd.Clear(); };
        ApplyStartup();
    }
    // True when the X should only hide Lotus (tray mode); the app then must not shut its services down.
    public bool WillHideToTray => !exiting && preferences.Current.CloseToTray && tray is not null;
    public void HideToTray(bool firstTime)
    {
        AppWindow.Hide();
        if (firstTime) { toldAboutTray = true; tray?.Notify(text["TrayTitle"], text["TrayBody"]); }
    }
    public void BringBack() => ShowFromTray(); // another launch asked for the window
    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        if (pendingReport is { } r && pendingGame is { } g) { pendingReport = null; _ = ShowReportAsync(g, r); }
    }
    private void ApplyStartup()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && Lotus.Infrastructure.Setup.WindowsStartup.IsEnabled() != preferences.Current.StartWithWindows)
                Lotus.Infrastructure.Setup.WindowsStartup.Set(preferences.Current.StartWithWindows, exe);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Start with Windows"); }
    }

    // Ctrl+Shift+L while playing: countdown and result right in the game overlay; details wait in Lotus.
    private async Task AnalyseRunningGameAsync()
    {
        if (analysing) return;
        var game = await Task.Run(games.RunningGame);
        if (game is null) { osd.Show(text["OsdNoGame"]); await Task.Delay(4000); osd.Clear(); return; }
        analysing = true; UiSound.Play(UiSound.Cue.Notify); // the in-game check starts
        try
        {
            const int seconds = 30, delay = 3;
            using var stop = new CancellationTokenSource();
            var countdown = Task.Run(async () =>
            {
                for (var left = seconds + delay; left > 0 && !stop.IsCancellationRequested; left--)
                {
                    osd.Show(left > seconds ? string.Format(text["OsdStarting"], left - seconds) : string.Format(text["OsdCounting"], left));
                    try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { }
                }
            });
            var report = await games.DiagnoseAsync(game, seconds, delay);
            stop.Cancel(); await countdown;
            if (report is null) { osd.Show(text["OsdFailed"]); UiSound.Play(UiSound.Cue.Fail); }
            else
            {
                var top = report.Findings[0];
                osd.Show(string.Format(text["OsdResult"], text[top.Key + "Title"])); UiSound.Play(UiSound.Cue.Done);
                pendingGame = game; pendingReport = report;
                tray?.Notify(text["TrayReportTitle"], text[top.Key + "Title"]);
            }
            await Task.Delay(9000);
        }
        finally { osd.Clear(); analysing = false; }
    }
    private async Task ShowReportAsync(Lotus.Core.Games.GameEntry game, Lotus.Core.Games.GamesViewModel.StutterReport report)
    {
        if (ShellRoot.XamlRoot is null) return;
        var body = new StackPanel { Spacing = 12, MaxWidth = 440 };
        body.Children.Add(new TextBlock { Text = report.Summary, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var f in report.Findings)
        {
            body.Children.Add(new TextBlock { Text = text[f.Key + "Title"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["GoldBrush"] });
            body.Children.Add(RichText.Block(text.Format(f.Key + "Body", f.Detail ?? ""), opacity: 0.9));
        }
        await new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = text.Format("WhyTitle", game.Name), Content = body, CloseButtonText = text["GsClose"] }.ShowAsync();
    }

    public void OpenOnboarding(bool edit)
    {
        Onboarding.MotionEnabled = !preferences.Current.ReduceMotion;
        Onboarding.Open(onboarding, edit);
    }
    // The profile badge opens the player panel (last game, favorites, notes); editing the profile is a button there.
    private void OnProfileClick(object sender, RoutedEventArgs e)
    {
        if (player is null) return;
        var p = preferences.Current;
        Player.Open(player, p.DisplayName is { Length: > 0 } n ? n : text["ObDefaultName"], p.AvatarPath);
        Player.ShowDiary(diaryPage);
    }
    private void OnPlayerEditProfile(object? sender, EventArgs e) => OpenOnboarding(edit: true);

    // ---- Game diary (any screen can open the editor or add a game to "Want to play") ----
    public Lotus.Core.Diary.GameDiary Diary => diary;
    public Lotus.Core.Diary.DiaryViewModel DiaryPage => diaryPage;
    public void OpenDiaryEditor(Lotus.Core.Diary.DiaryEntry game, Lotus.Core.Diary.DiaryStatus? status = null)
    {
        Player.Close();
        int? steamMinutes = null;
        if (game.AppId is { } id) { try { steamMinutes = Lotus.Infrastructure.Games.SteamPlaytime.Read().GetValueOrDefault(id) is var m && m > 0 ? m : null; } catch (IOException) { } }
        diaryEditor.Open(game, steamMinutes, status);
        DiaryEditor.Open(diaryEditor);
    }
    // "Quero jogar": adds quietly with a small chime; false when the game was already in the diary.
    public bool WantGame(Lotus.Core.Diary.DiaryEntry game)
    {
        var change = diary.Want(game);
        if (change is null) return false;
        if (change.WorthCelebrating) Celebration.Show(change, text); else UiSound.Play(UiSound.Cue.Done);
        return true;
    }
    private void OnDiarySaved(object? sender, Lotus.Core.Diary.DiaryProgressChange change)
    {
        if (change.WorthCelebrating) Celebration.Show(change, text);
        else UiSound.Play(UiSound.Cue.Done);
    }
    private void OnOpenDiary(object? sender, EventArgs e) { Player.Close(); ViewModel.NavigateCommand.Execute("diary"); }
    // Title-bar diary chip: opens the diary page and shows the player's level.
    private void OnDiaryButton(object sender, RoutedEventArgs e) { Motion.Pop(DiaryIcon); UiSound.Play(UiSound.Cue.Navigate); OnOpenDiary(sender, EventArgs.Empty); }
    private void ShowDiaryLevel() { if (DiaryLevel is { } t && diaryPage is { } d) t.Text = d.LevelText; }
    private void OnDiaryEnter(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => DiaryButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x4D, 0xF2, 0x7B, 0xA8));
    private void OnDiaryLeave(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => DiaryButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x2E, 0xF2, 0x7B, 0xA8));
    // Hover feedback on the profile badge (it opens the player panel).
    private void OnProfileEnter(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if (sender is Border b) b.Background = (Brush)Application.Current.Resources["CardHoverBrush"]; }
    private void OnProfileLeave(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if (sender is Border b) b.Background = (Brush)Application.Current.Resources["CardBrush"]; }
    // Corner badge: the player's chosen name and picture (Steam or their own), "Player" until chosen.
    private void ShowProfile()
    {
        var p = preferences.Current;
        var name = p.DisplayName.Length > 0 ? p.DisplayName : "Player";
        ProfileName.Text = name; ProfileInitial.Text = char.ToUpperInvariant(name[0]).ToString();
        ToolTipService.SetToolTip(ExpandedFooter, text["ProfileEdit"]);
        if (p.AvatarPath is { } path && File.Exists(path))
        {
            ProfilePictureBrush.ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path)) { DecodePixelWidth = 64 };
            ProfilePicture.Visibility = Visibility.Visible;
        }
        else ProfilePicture.Visibility = Visibility.Collapsed;
    }

    // After a "Preparar e jogar" session: ask (never decide) whether to return to the previous settings.
    private async void OnGameSessionEnded(object? sender, Lotus.Core.Games.PlaySessionEnded session)
    {
        if (!session.ProfileApplied || ShellRoot.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ElementTheme.Dark, DefaultButton = ContentDialogButton.Primary,
            Title = text.Format("PlayEndedTitle", session.Game.Name),
            Content = RichText.Block(text["PlayEndedBody"]),
            PrimaryButtonText = text["PlayUndo"], CloseButtonText = text["PlayKeep"]
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await games.UndoProfileAsync();
    }
    private void UpdateMonitoringDemand()
    {
        if (!initialized) return;
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        if (!visible) experience.Interrupt();
        var demand = MonitoringDemand.For(ViewModel.CurrentRoute, visible, experience.IsAnalyzing);
        memoryAnalysis.SetActive(demand.Analyze);
        monitoring.SetActive(demand.Collect);
    }
    public void PrepareForClose()
    {
        initialized = false;
        experience.Interrupt();
        AmbientBackground.Dispose();
        brandProof?.Dispose(); brandProof = null;
        memoryAnalysis.SetActive(false);
        monitoring.SetActive(false);
        StopBrandMotion();
    }
}
