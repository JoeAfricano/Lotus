using Lotus.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Globalization;

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

    public MainWindow(ShellViewModel viewModel, ITextCatalog text, PreferencesService preferences, PreferencesViewModel preferencesViewModel, ThemePalette palette)
    {
        ViewModel = viewModel;
        this.text = text;
        this.preferences = preferences;
        this.preferencesViewModel = preferencesViewModel;
        this.palette = palette;
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
            StopBrandMotion();
            preferences.PropertyChanged -= OnPreferencesChanged;
        };
        Activated += (_, args) =>
        {
            windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
            UpdateBrandMotion();
        };
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
            ContentFrame.Navigate(typeof(SectionPage), new SectionViewModel(ViewModel.SelectedSection, text, preferencesViewModel, false), new SuppressNavigationTransitionInfo());
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
    }
    private void UpdateBrandMotion()
    {
        if (!initialized || ShellRoot.XamlRoot is null) return;
        if (!windowActive || preferences.Current.ReduceMotion || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            StopBrandMotion();
            return;
        }
        titlePulse ??= new BrandPulse(TitleLogo);
        if (Navigation.IsPaneOpen) sidebarPulse ??= new BrandPulse(BrandLogo);
        else { sidebarPulse?.Dispose(); sidebarPulse = null; }
    }
    private void StopBrandMotion()
    {
        titlePulse?.Dispose(); titlePulse = null;
        sidebarPulse?.Dispose(); sidebarPulse = null;
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
        ContentFrame.Navigate(typeof(SectionPage), new SectionViewModel(ViewModel.SelectedSection, text, preferencesViewModel, !preferences.Current.ReduceMotion), new SuppressNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
        displayedSectionId = id;
        if (narrow == true) Navigation.IsPaneOpen = false;
    }
}
