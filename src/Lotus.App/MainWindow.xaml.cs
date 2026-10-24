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
    private bool windowActive;
    private BrandPulse? titlePulse;
    private BrandPulse? sidebarPulse;
    private BrandPulse? flowerPulse;
    private readonly MonitoringViewModel monitoring;
    private bool? brandAnimated;

    public MainWindow(ShellViewModel viewModel, ITextCatalog text, PreferencesService preferences, PreferencesViewModel preferencesViewModel, ThemePalette palette, MonitoringViewModel monitoring)
    {
        ViewModel = viewModel;
        this.text = text;
        this.preferences = preferences;
        this.preferencesViewModel = preferencesViewModel;
        this.palette = palette;
        this.monitoring = monitoring;
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
        Navigation.SelectedItem = Navigation.MenuItems[0];
        preferences.PropertyChanged += OnPreferencesChanged;
        Closed += (_, _) =>
        {
            initialized = false;
            monitoring.SetActive(false);
            StopBrandMotion();
            preferences.PropertyChanged -= OnPreferencesChanged;
        };
        Activated += (_, args) =>
        {
            windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
            UpdateBrandMotion();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateMonitoringDemand);
        };
        AppWindow.Changed += (_, _) => { UpdateMonitoringDemand(); UpdateBrandMotion(); };
        TitleLogo.SizeChanged += (_, _) => UpdateBrandMotion();
        BrandLogo.SizeChanged += (_, _) => UpdateBrandMotion();
        BrandFlower.SizeChanged += (_, _) => UpdateBrandMotion();
        ShellRoot.Loaded += (_, _) =>
        {
            UpdateNavigationMode(ShellRoot.ActualWidth);
            UpdateMinimumSize();
            UpdatePaneChrome();
            UpdateBrandMotion();
            ShellRoot.XamlRoot.Changed += (_, _) => UpdateMinimumSize();
        };
    }
    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PreferencesService.Current)) return;
        palette.Apply(preferences.Current.Theme);
        UpdateBrandMotion();
        if (text.Language == preferences.Current.Language) return;
        text.SetLanguage(preferences.Current.Language);
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(text.Language);
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(text.Language);
        ShellRoot.Language = text.Language;
        ViewModel.RefreshLanguage();
        monitoring.RefreshLanguage();
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
            ContentFrame.Navigate(typeof(SectionPage), new SectionViewModel(ViewModel.SelectedSection, text, preferencesViewModel, false, monitoring), new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
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
    private void UpdateBrandMotion()
    {
        if (!initialized || ShellRoot.XamlRoot is null) return;
        var animate = AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized }
            && !preferences.Current.ReduceMotion && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        if (brandAnimated != animate) { StopBrandMotion(); brandAnimated = animate; }
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) { StopBrandMotion(); return; }
        titlePulse ??= BrandPulse.TryCreate(TitleLogo, animate);
        if (Navigation.IsPaneOpen)
        {
            sidebarPulse ??= BrandPulse.TryCreate(BrandLogo, animate);
            flowerPulse ??= BrandPulse.TryCreate(BrandFlower, animate, flower: true);
        }
        else { sidebarPulse?.Dispose(); sidebarPulse = null; flowerPulse?.Dispose(); flowerPulse = null; }
    }
    private void StopBrandMotion()
    {
        titlePulse?.Dispose(); titlePulse = null;
        sidebarPulse?.Dispose(); sidebarPulse = null;
        flowerPulse?.Dispose(); flowerPulse = null;
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
        if (args.SelectedItem is not NavigationViewItem { Tag: string id } || displayedSectionId == id) return;
        ViewModel.NavigateCommand.Execute(id);
        if (ViewModel.SelectedSection.Id != id) return;
        ContentFrame.Navigate(typeof(SectionPage), new SectionViewModel(ViewModel.SelectedSection, text, preferencesViewModel, !preferences.Current.ReduceMotion, monitoring), new SuppressNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
        displayedSectionId = id;
        UpdateMonitoringDemand();
        if (narrow == true) Navigation.IsPaneOpen = false;
    }
    private void UpdateMonitoringDemand()
    {
        if (!initialized) return;
        var minimized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        monitoring.SetActive(displayedSectionId == "monitoring" && !minimized && AppWindow.IsVisible);
    }
    public void PrepareForClose()
    {
        initialized = false;
        monitoring.SetActive(false);
        StopBrandMotion();
    }
}
