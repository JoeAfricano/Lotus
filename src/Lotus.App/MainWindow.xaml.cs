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
        foreach (var section in ViewModel.Items)
        {
            var item = new NavigationViewItem
            {
                Content = NavLabel(section), Tag = section.Id,
                Icon = new FontIcon { Glyph = section.Glyph, FontSize = 17 },
                MinHeight = 44, Margin = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(10), HorizontalContentAlignment = HorizontalAlignment.Stretch
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
        UiSound.Enabled = preferences.Current.UiSounds;
        ShowProfile();
        if (onboarding.NeedsWelcome) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => OpenOnboarding(edit: false));
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
        UiSound.Enabled = preferences.Current.UiSounds;
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
            item.Content = NavLabel(section);
            AutomationProperties.SetName(item, section.Title);
            ToolTipService.SetToolTip(item, section.Title);
        }
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
    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (updatingNavigation || args.SelectedItem is not NavigationViewItem { Tag: string id }) return;
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
        Navigation.SelectedItem = Navigation.MenuItems.Cast<NavigationViewItem>().First(item => (string)item.Tag == group);
        updatingNavigation = false;
        if (force || displayedSectionId != group)
        {
            ContentFrame.Navigate(typeof(SectionPage), new SectionViewModel(ViewModel.SelectedSection, text, preferencesViewModel,
                !preferences.Current.ReduceMotion, monitoring, memoryAnalysis, ViewModel, experience, benchmark, optimizer, powerProfiles, hardware, games, integrations, score, tools), new SuppressNavigationTransitionInfo());
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
    public void HideToTray(bool firstTime)
    {
        AppWindow.Hide();
        if (firstTime) { toldAboutTray = true; tray?.Notify(text["TrayTitle"], text["TrayBody"]); }
    }
    private void ShowFromTray()
    {
        AppWindow.Show(); Activate();
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
        analysing = true;
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
    private void OnProfileTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => OpenOnboarding(edit: true);
    // Hover feedback on the profile badge (it opens the profile editor).
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
