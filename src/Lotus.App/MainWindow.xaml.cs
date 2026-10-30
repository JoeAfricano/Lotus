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
    private Lotus.Core.Achievements.TrophyCenter trophies = null!;
    private Lotus.Core.Achievements.AchievementsViewModel achievements = null!;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? steamTimer;
    private Lotus.Core.Backup.BackupViewModel backup = null!;
    private static string AppVersionText() =>
        (System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion.Split('+')[0] ?? "";

    // Starts a fresh Lotus and closes this one (after a restore). The new one waits for this one to go.
    public void Restart()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, Environment.GetCommandLineArgs().Contains("--dev-instance") ? "--restarted --dev-instance" : "--restarted") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Restart"); }
        Application.Current.Exit();
    }
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
        // "Onde eu parei?": one store for the diary editor, Home, the player panel and the end-of-session notice.
        resume = new Lotus.Core.Diary.ResumeNotes(new Lotus.Infrastructure.Games.JsonResumeNoteStore(), ex => Serilog.Log.Warning(ex, "Resume notes"));
        var journal = new Lotus.Core.Games.PlayerJournal(new Lotus.Infrastructure.Games.JsonPlayerJournalStore(), ex => Serilog.Log.Warning(ex, "Player journal"));
        journal.Resume = resume;
        games.IsFavorite = journal.IsFavorite;
        journal.Changed += (_, _) => games.RefreshFeatured();
        player = new Lotus.Core.Games.PlayerPanelViewModel(games, journal, text, () => Lotus.Infrastructure.Games.SteamPlaytime.Read());
        var catalog = new Lotus.Infrastructure.Catalog.SteamCatalog(language: preferences.Current.Language);
        diary = new Lotus.Core.Diary.GameDiary(new Lotus.Infrastructure.Games.JsonGameDiaryStore(), ex => Serilog.Log.Warning(ex, "Game diary"));
        diaryPage = new Lotus.Core.Diary.DiaryViewModel(diary, text, preferences);
        diaryPage.Deals = new Lotus.Core.Diary.DiaryDealsViewModel(diary, catalog, text, ex => Serilog.Log.Warning(ex, "Diary deals"));
        diaryEditor = new Lotus.Core.Diary.DiaryEditorViewModel(diary, text);
        diaryEditor.Resume = resume;
        diary.Changed += (_, _) => DispatcherQueue.TryEnqueue(ShowDiaryLevel);
        diary.ProgressChanged += (_, _) => DispatcherQueue.TryEnqueue(ShowDiaryLevel);
        // Achievements: Lotus ones, Steam ones read from this PC, all feeding the diary level.
        trophies = new Lotus.Core.Achievements.TrophyCenter(diary, new Lotus.Infrastructure.Games.SteamAchievementReader(preferences.Current.Language),
            new Lotus.Infrastructure.Games.SteamRarityClient(), new Lotus.Infrastructure.Games.JsonTrophyLedgerStore(),
            () => Lotus.Infrastructure.Games.SteamPlaytime.Read(), () => games.InstalledGames().Count, ex => Serilog.Log.Warning(ex, "Achievements"),
            async (id, token) => (await catalog.SteamDetailsAsync(id, token))?.Name);
        achievements = new Lotus.Core.Achievements.AchievementsViewModel(trophies, text);
        diaryPage.Achievements = achievements;
        // Backup: a file on this PC and, when this build has a Google client, Google Drive.
        backup = new Lotus.Core.Backup.BackupViewModel(new Lotus.Infrastructure.Backup.LotusBackupArchive(appVersion: AppVersionText()),
            new Lotus.Infrastructure.Backup.GoogleDriveBackup(Lotus.Infrastructure.Backup.GoogleOAuthConfig.Load(), new WindowsTokenVault(),
                async uri => await Windows.System.Launcher.LaunchUriAsync(uri)),
            text, ex => Serilog.Log.Warning(ex, "Backup"));
        gameList = new Lotus.Core.Catalog.GameListViewModel(catalog, text, ex => Serilog.Log.Warning(ex, "Game list"), games.InstalledGames);
        gameDetails = new Lotus.Core.Catalog.GameDetailsViewModel(catalog, text, ex => Serilog.Log.Warning(ex, "Game details"));
        // Preparar e jogar: same Game profile and journal as Otimizar; the game is followed until it closes.
        games.GameProfile = new Lotus.Core.Optimization.GameProfileAdapter(powerProfiles);
        games.Watch = sessionHub; // the light-mode check, shared (no second polling loop)
        games.Memory = new Lotus.Infrastructure.Games.WindowsMemoryPressure();
        SetUpPreparation();
        SetUpSaves();
        SetUpUpdates();
        games.TechScanner = new Lotus.Infrastructure.Games.GameTechScanner();
        var overlayScanner = new Lotus.Infrastructure.Games.WindowsOverlayScanner();
        games.Overlays = overlayScanner;
        tools.Overlays = new Lotus.Core.Games.OverlayCheckViewModel(overlayScanner, text);
        games.Rtss = integrations.Rtss;
        games.AdaptiveSync = () => preferences.Current.AdaptiveSync;
        games.Display = new Lotus.Infrastructure.Games.WindowsDisplaySync();
        games.ScoreLine = (g, r, pc) =>
        {
            var res = pc.Height >= 2000 ? Lotus.Core.Score.Resolution.P2160 : pc.Height >= 1400 ? Lotus.Core.Score.Resolution.P1440 : Lotus.Core.Score.Resolution.P1080;
            var preset = (Lotus.Core.Score.Preset)(int)r.Tier;
            return score.EstimateFor(g.Name, g.Store == Lotus.Core.Games.GameStores.Steam ? g.Id : null, res, preset) is { Fps: > 0 } cell
                ? text.Format("TuneScoreLine", cell.Fps, text["ScoreRes" + res], text["ScorePreset" + preset]) : null;
        };
        games.ScoreHint = () => text[score.HasFullTest ? "TuneScoreSearch" : "TuneScoreHint"];
        games.Goal = () => preferences.Current.Goal;
        _ = games.EnsureLoadedAsync(); // Home shows game shortcuts right away
        SetUpBackground();
        try { experience.QuickFacts = Lotus.Infrastructure.Tools.QuickHardwareFacts.Read(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Quick hardware facts"); }
        games.SessionEnded += OnGameSessionEnded;
        games.LaunchFailed += (_, message) => DispatcherQueue.TryEnqueue(() => { UiSound.Play(UiSound.Cue.Fail); ShowWarning(message); });
        // Lotus Score: sensors during the tests (3DMark-style) and the player's games for the FPS estimates.
        score.Sensors = () => { var s = monitoring.Snapshot; var g = s?.Gpu.Devices.FirstOrDefault(); return (s?.Cpu.TotalUtilizationPercent.Value, g?.Get(Lotus.Core.Telemetry.GpuSensor.Temperature)?.Value, g?.Get(Lotus.Core.Telemetry.GpuSensor.Power)?.Value); };
        score.FindGame = (q, t) => catalog.RequirementsAsync(q, t);
        score.InstalledGames = () => games.InstalledGames().Select(g => (g.Name, g.Store == Lotus.Core.Games.GameStores.Steam ? (string?)g.Id : null));
        score.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(score.Busy)) DispatcherQueue.TryEnqueue(() => { UpdateMonitoringDemand(); UpdateLightMode(); }); };
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
        // Light / dark / system: the whole window and its dialogs follow the palette live.
        palette.Dispatcher = DispatcherQueue;
        palette.Changed += (_, _) => ApplyElementTheme();
        ApplyElementTheme();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        foreach (var section in ViewModel.Items.OrderBy(s => s.Id is "gamelist" or "saves" or "diary" ? 1 : 0)) // design order: Game list and the diary close the list; Settings sits at the bottom
        {
            var item = new NavigationViewItem
            {
                Content = NavLabel(section), Tag = section.Id,
                Icon = section.Id == "diary" ? new FontIcon { Glyph = section.Glyph, FontSize = 17, Foreground = (Brush)Application.Current.Resources["GoldBrush"] } : new FontIcon { Glyph = section.Glyph, FontSize = 17 },
                MinHeight = 44, Margin = new Thickness(1, 2, 1, 2), CornerRadius = new CornerRadius(10), HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(item, section.Title);
            AutomationProperties.SetAutomationId(item, "nav-" + section.Id);
            ToolTipService.SetToolTip(item, section.Title);
            if (section.Id == "settings") Navigation.FooterMenuItems.Add(item); else Navigation.MenuItems.Add(item);
            if (section.Id == "diary") { diaryNav = item; item.InfoBadge = new InfoBadge { Value = diaryPage.Progress.Level }; AutomationProperties.SetHelpText(item, diaryPage.LevelText); }
        }
        Navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => OnPaneStateChanged());
        initialized = true;
        StartLightMode();
        UpdatePaneChrome();
        if (preferences.LoadStatus is not ("Loaded" or "Missing")) ShowWarning(text["LoadWarning"]);
        ViewModel.PropertyChanged += OnRouteChanged;
        experience.DemandChanged += OnExperienceDemandChanged;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        // Player profile: accent color, corner badge and the first-run welcome.
        onboarding = new Lotus.Core.Profile.OnboardingViewModel(preferences, new Lotus.Infrastructure.Profile.LocalProfileSource(), text, ex => Serilog.Log.Warning(ex, "Profile"),
            new Lotus.Core.Profile.RecommendedAppsViewModel(new Lotus.Infrastructure.Profile.WingetAppInstaller(), text, ex => Serilog.Log.Warning(ex, "Recommended apps")));
        onboarding.AccentPreview += (_, accent) => palette.ApplyAccent(accent);
        onboarding.ThemePreview += (_, theme) => palette.Apply(theme);
        onboarding.CustomThemePreview += (_, spec) => palette.Preview(spec);
        Onboarding.Closed += (_, _) => { palette.Apply(preferences.Current.Theme, preferences.Current.ActiveCustom()); palette.ApplyAccent(preferences.Current.Accent); ShowProfile(); ShowRoute(force: true); };
        palette.ApplyAccent(preferences.Current.Accent);
        UiSound.Volume = preferences.Current.UiSoundVolume / 100.0; UiSound.Enabled = preferences.Current.UiSounds;
        ShowProfile();
        if (onboarding.NeedsWelcome) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => OpenOnboarding(edit: false));
        else DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (AppWindow.IsVisible) UiSound.Play(UiSound.Cue.Startup); }); // soft hello (not when starting in the tray)
        ShowRoute();
        StartAchievements();
        StartDealsCheck();
        preferences.PropertyChanged += OnPreferencesChanged;
        Closed += (_, _) =>
        {
            EndGameWatch();
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
            UpdateLightMode();
            if (windowActive && trophies.LastSync is { } synced && DateTimeOffset.Now - synced > TimeSpan.FromMinutes(3)) _ = trophies.SyncAsync(); // back from a game: new achievements?
            UpdateAmbientState();
            // Deactivation alone does not interrupt an explicit analysis.
            UpdateBrandMotion();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateMonitoringDemand);
        };
        AppWindow.Changed += (_, _) => { UpdateMonitoringDemand(); UpdateBrandMotion(); UpdateAmbientState(); UpdateLightMode(); };
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
    private void ApplyElementTheme()
    {
        ShellRoot.RequestedTheme = ThemePalette.Element;
        // The gold name keeps its effects in every theme; light gets a deeper gold (redrawn on a theme change).
        if (BrandPulse.LightTheme != (ThemePalette.Element == ElementTheme.Light)) { BrandPulse.LightTheme = ThemePalette.Element == ElementTheme.Light; StopBrandMotion(); brandAnimated = null; }
        UpdateBrandMotion();
        var light = ThemePalette.Element == ElementTheme.Light;
        AmbientBackground.Opacity = light ? 0 : 1; // design: the ambient art barely shows on porcelain, so names keep their contrast
        ApplyBackdrop();
        var tb = AppWindow.TitleBar;
        tb.ButtonForegroundColor = light ? Windows.UI.Color.FromArgb(255, 40, 28, 48) : Windows.UI.Color.FromArgb(255, 245, 239, 243);
        tb.ButtonInactiveForegroundColor = light ? Windows.UI.Color.FromArgb(255, 120, 104, 130) : Windows.UI.Color.FromArgb(255, 150, 130, 142);
        tb.ButtonHoverBackgroundColor = light ? Windows.UI.Color.FromArgb(0x14, 40, 28, 48) : Windows.UI.Color.FromArgb(0x1F, 255, 255, 255);
        tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
    }
    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PreferencesService.Current)) return;
        palette.Apply(preferences.Current.Theme, preferences.Current.ActiveCustom());
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
        foreach (var item in NavItems())
        {
            var section = ViewModel.Items.Single(s => s.Id == (string)item.Tag);
            item.Content = NavLabel(section);
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
        // Side navigation (design): full menu on wide windows, icons only on medium ones, a collapsible menu on small ones.
        var mode = width >= 1100 ? NavigationViewPaneDisplayMode.Left : width >= 720 ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.LeftMinimal;
        var nowNarrow = mode != NavigationViewPaneDisplayMode.Left;
        if (narrow == nowNarrow && Navigation.PaneDisplayMode == mode) return;
        updatingLayout = true;
        narrow = nowNarrow;
        Navigation.PaneDisplayMode = mode;
        Navigation.IsPaneOpen = mode == NavigationViewPaneDisplayMode.Left;
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
        // Open side menu: the brand sits at its top (title bar shows the tagline); closed: the title bar keeps the brand.
        BrandHeader.Visibility = open && !top ? Visibility.Visible : Visibility.Collapsed;
        TitleBrand.Visibility = BrandHeader.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ExpandedFooter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        CompactFooter.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        DiaryButton.Visibility = Visibility.Collapsed; // "Meu diário" is a menu item now (with the level badge)
        ShowDiaryLevel();
        UpdateToggleAccessibility(Navigation, text[open ? "CloseNavigation" : "OpenNavigation"]);
        UpdateBrandMotion();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateBrandMotion);
    }
    private void UpdateAmbientState()
    {
        if (!initialized) return;
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        AmbientBackground.SetWindowState(windowActive, visible && AmbientBackground.Visibility == Visibility.Visible, preferences.Current.ReduceMotion); // hidden by a custom background: it stops drawing
        ThemeScene.SetState(visible && windowActive, preferences.Current.ReduceMotion || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled);
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
        Navigation.SelectedItem = NavItems().FirstOrDefault(item => (string)item.Tag == group);
        if (Navigation.SelectedItem is null) foreach (var i in NavItems()) i.IsSelected = false;
        // The control leaves its underline on the last item when nothing is selected: hide it on the Game list.
        if (Application.Current.Resources["NavigationViewSelectionIndicatorForeground"] is SolidColorBrush indicator)
            indicator.Color = Navigation.SelectedItem is null ? Microsoft.UI.Colors.Transparent : ((SolidColorBrush)Application.Current.Resources["RoseBrush"]).Color;
        updatingNavigation = false;
        if (force || displayedSectionId != group)
        {
            ContentFrame.Navigate(typeof(SectionPage), new SectionViewModel(ViewModel.SelectedSection, text, preferencesViewModel,
                !preferences.Current.ReduceMotion, monitoring, memoryAnalysis, ViewModel, experience, benchmark, optimizer, powerProfiles, hardware, games, integrations, score, tools) { Guided = guided, GameList = gameList, Saves = saveLibrary, GameDetails = gameDetails, Diary = diaryPage, Backup = backup, Updates = updates }, new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear(); displayedSectionId = group;
            Motion.EnterPage(ContentFrame); // new section rises and fades in
        }
        UpdateMonitoringDemand();
    }
    private void OnExperienceDemandChanged(object? sender, EventArgs args) => UpdateMonitoringDemand();

    // "Meu diário" is a main part of Lotus: its menu label and icon stand out in gold, like the name.
    private IEnumerable<NavigationViewItem> NavItems() => Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>();
    private NavigationViewItem? diaryNav;
    private static object NavLabel(Section section) => section.Id != "diary" ? section.Title
        : new TextBlock { Text = section.Title, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["GoldBrush"] };

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
        analysing = true; UpdateLightMode(); UiSound.Play(UiSound.Cue.Notify); // the in-game check starts
        try
        {
            const int seconds = 30, delay = 3;
            using var stop = new CancellationTokenSource();
            var countdown = Task.Run(async () =>
            {
                for (var left = seconds + delay; left > 0 && !stop.IsCancellationRequested; left--)
                {
                    osd.Show(left > seconds ? string.Format(text["OsdStarting"], left - seconds) : string.Format(text["OsdCounting"], left, OsdBar(seconds - left, seconds)));
                    try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { }
                }
            });
            var report = await games.DiagnoseAsync(game, seconds, delay);
            stop.Cancel(); await countdown;
            if (report is null) { osd.Show(text["OsdFailed"]); UiSound.Play(UiSound.Cue.Fail); }
            else
            {
                var top = report.Findings[0];
                var numbers = string.Format(System.Globalization.CultureInfo.GetCultureInfo(text.Language), text["OsdNumbers"], report.AverageFps, report.Low1Fps, report.Spikes);
                osd.Show(string.Format(text["OsdResult"], text[top.Key + "Title"], numbers, text[top.Key + "Tip"])); UiSound.Play(UiSound.Cue.Done);
                pendingGame = game; pendingReport = report;
                tray?.Notify(text["TrayReportTitle"], text[top.Key + "Title"]);
            }
            await Task.Delay(9000);
        }
        finally { osd.Clear(); analysing = false; UpdateLightMode(); }
    }
    // In-game progress bar for RivaTuner's OSD: 24 bars, rose for the measured part, dim plum for the rest.
    private static string OsdBar(int done, int total)
    {
        const int width = 24;
        var filled = total <= 0 ? 0 : Math.Clamp((int)Math.Round(width * (double)done / total), 0, width);
        return "<C=F27BA8>" + new string('|', filled) + "<C><C=4A3A44>" + new string('|', width - filled) + "<C>";
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
        await new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ThemePalette.Element, Title = text.Format("WhyTitle", game.Name), Content = body, CloseButtonText = text["GsClose"] }.ShowAsync();
    }

    public Controls.GameTunePanel Tune => TunePanel;
    // FreeSync/G-Sync answer used by the game settings ("on" | "off" | null = not asked yet).
    public string? AdaptiveSync { get => preferences.Current.AdaptiveSync; set => preferences.Update(preferences.Current with { AdaptiveSync = value }); }
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
    // Yearly recap (from the diary's Statistics tab).
    public void OpenDiaryRecap(int year)
    {
        var recap = new Lotus.Core.Diary.DiaryRecapViewModel(diary, text);
        recap.Open(year, diaryPage.Name);
        DiaryRecap.Open(recap);
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
    // Achievements: toasts for anything new, a first look at Steam a few seconds after start, then every 10 minutes.
    private void StartAchievements()
    {
        trophies.News += (_, news) => DispatcherQueue.TryEnqueue(() => Toast.Show(news, text));
        trophies.Evaluate();
        if (DateTime.Now.Hour == 0) Lotus.Core.Achievements.Activity.Record(Lotus.Core.Achievements.Activity.Midnight);
        steamTimer = DispatcherQueue.CreateTimer();
        steamTimer.Interval = TimeSpan.FromSeconds(4);
        steamTimer.Tick += (t, _) => { t.Interval = TimeSpan.FromMinutes(10); _ = trophies.SyncAsync(); };
        steamTimer.Start();
    }
    private void OnToastView(object? sender, bool steam)
    {
        Player.Close();
        diaryPage.Section = steam ? "steam" : "trophies";
        ViewModel.NavigateCommand.Execute("diary");
    }
    public Lotus.Core.Achievements.TrophyCenter Trophies => trophies;
    // From a game page: the diary's Steam section, opened on that game.
    public void ShowSteamAchievements(string appId)
    {
        Player.Close();
        achievements.SteamQuery = "";
        achievements.Select(achievements.SteamGames.FirstOrDefault(g => g.Game.AppId == appId));
        diaryPage.Section = "steam";
        ViewModel.NavigateCommand.Execute("diary");
    }

    // Title-bar diary chip: opens the diary page and shows the player's level.
    private void OnDiaryButton(object sender, RoutedEventArgs e) { Motion.Pop(DiaryIcon); UiSound.Play(UiSound.Cue.Navigate); OnOpenDiary(sender, EventArgs.Empty); }
    private void ShowDiaryLevel()
    {
        if (DiaryLevel is { } t && diaryPage is { } d) t.Text = d.LevelText;
        if (diaryNav?.InfoBadge is { } badge && diaryPage is { } p) { badge.Value = p.Progress.Level; AutomationProperties.SetHelpText(diaryNav, p.LevelText); }
    }
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
            XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ThemePalette.Element, DefaultButton = ContentDialogButton.Primary,
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
        monitoring.SetActive(demand.Collect || score.Busy); // the Score reads temperature/power while its tests run
    }
    public void PrepareForClose()
    {
        lightTimer?.Stop();
        initialized = false;
        experience.Interrupt();
        AmbientBackground.Dispose();
        brandProof?.Dispose(); brandProof = null;
        memoryAnalysis.SetActive(false);
        monitoring.SetActive(false);
        StopBrandMotion();
    }
}
