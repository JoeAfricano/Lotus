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
    private bool initialized;
    private bool updatingLayout;
    private bool? narrow;
    private string? displayedSectionId;
    private bool updatingNavigation;
    private bool windowActive;
    private readonly ExperienceViewModel experience;
    private readonly Lotus.Core.Benchmark.BenchmarkViewModel benchmark;
    private readonly Lotus.Core.Optimization.OptimizationViewModel optimizer;
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

    public MainWindow(ShellViewModel viewModel, ITextCatalog text, PreferencesService preferences, PreferencesViewModel preferencesViewModel, ThemePalette palette, MonitoringViewModel monitoring, Lotus.Core.Diagnostics.MemoryAnalysisViewModel memoryAnalysis, ExperienceViewModel experience, Lotus.Core.Benchmark.BenchmarkViewModel benchmark, Lotus.Core.Optimization.OptimizationViewModel optimizer)
    {
        ViewModel = viewModel;
        this.text = text;
        this.preferences = preferences;
        this.preferencesViewModel = preferencesViewModel;
        this.palette = palette;
        this.monitoring = monitoring;
        this.memoryAnalysis = memoryAnalysis;
        this.experience = experience; this.benchmark = benchmark; this.optimizer = optimizer;
        InitializeComponent();
        ShellRoot.DataContext = ViewModel;
        ShellRoot.Language = text.Language;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 16, 16, 22);
        AppWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 245, 239, 243);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        foreach (var section in ViewModel.Items)
        {
            var item = new NavigationViewItem
            {
                Content = section.Title, Tag = section.Id,
                Icon = new FontIcon { Glyph = section.Glyph, FontSize = 18 },
                MinHeight = 42, HorizontalContentAlignment = HorizontalAlignment.Stretch
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
        UpdateAmbientState();
        UpdateBrandMotion();
        if (text.Language == preferences.Current.Language) return;
        text.SetLanguage(preferences.Current.Language);
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(text.Language);
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(text.Language);
        ShellRoot.Language = text.Language;
        ViewModel.RefreshLanguage();
        monitoring.RefreshLanguage();
        experience.RefreshLanguage(); benchmark.RefreshLanguage(); optimizer.RefreshLanguage();
        foreach (NavigationViewItem item in Navigation.MenuItems)
        {
            var section = ViewModel.Items.Single(s => s.Id == (string)item.Tag);
            item.Content = section.Title;
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
        var nowNarrow = width < 1050;
        if (narrow == nowNarrow) return;
        updatingLayout = true;
        narrow = nowNarrow;
        Navigation.PaneDisplayMode = nowNarrow ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
        Navigation.IsPaneOpen = !nowNarrow && preferences.Current.SidebarExpanded;
        updatingLayout = false;
        UpdatePaneChrome();
    }
    private void OnPaneStateChanged()
    {
        UpdatePaneChrome();
        // Overlay transitions on small windows must not overwrite the desktop preference.
        if (!initialized || updatingLayout || narrow != false) return;
        if (!preferences.Update(preferences.Current with { SidebarExpanded = Navigation.IsPaneOpen }))
            ShowWarning(text["SidebarSaveFailed"]);
    }
    private void UpdatePaneChrome()
    {
        var open = Navigation.IsPaneOpen;
        BrandHeader.Visibility = ExpandedFooter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
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
        if (narrow == true) Navigation.IsPaneOpen = false;
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
                !preferences.Current.ReduceMotion, monitoring, memoryAnalysis, ViewModel, experience, benchmark, optimizer), new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear(); displayedSectionId = group;
        }
        UpdateMonitoringDemand();
    }
    private void OnExperienceDemandChanged(object? sender, EventArgs args) => UpdateMonitoringDemand();
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
