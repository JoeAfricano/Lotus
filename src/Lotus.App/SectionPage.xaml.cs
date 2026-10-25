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
            ShowPerformance = vm.Section.Id == "performance"; ShowGames = vm.Section.Id == "games"; ShowSettings = vm.Section.Id == "settings";
            ShowDiagnosis = ShowOptimize && shell.OptimizeTabIndex == 0;
            ShowRestore = ShowOptimize && shell.OptimizeTabIndex == 1;
            ShowLive = ShowPerformance && shell.PerformanceTabIndex == 0;
            ShowCompare = ShowPerformance && shell.PerformanceTabIndex == 1; ShowHistory = ShowPerformance && shell.PerformanceTabIndex == 2;
            ShowAppearance = ShowSettings && shell.SettingsCategoryIndex == 0;
            ShowMonitoringSettings = ShowSettings && shell.SettingsCategoryIndex == 1;
            ShowIntegrations = ShowSettings && shell.SettingsCategoryIndex == 2;
            ShowSafety = ShowSettings && shell.SettingsCategoryIndex == 3; ShowAdvanced = ShowSettings && shell.SettingsCategoryIndex == 4;
            ShowFaq = ShowSettings && shell.SettingsCategoryIndex == 5;
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
        if (CpuSummary is null || GpuSummary is null || RamSummary is null) return;
        var stacked = e.NewSize.Width < 720;
        foreach (var (card, index) in new[] { (CpuSummary, 0), (GpuSummary, 1), (RamSummary, 2) })
        {
            Grid.SetColumn(card, stacked ? 0 : index); Grid.SetRow(card, stacked ? index : 0); Grid.SetColumnSpan(card, stacked ? 3 : 1);
        }
    }
}
