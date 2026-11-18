using System.ComponentModel;
using System.Collections.Specialized;
using Lotus.Core.Telemetry;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lotus.App.Controls;

// Otimizar › "Ajuste da placa": choose a goal, connect Afterburner, test candidates, apply a passed result and opt into
// game-only use. Manual values and detailed instructions remain available in the advanced section.
public sealed class GpuTunePanel : UserControl
{
    private GpuTuneViewModel? vm;
    private bool subscribed, renderQueued;
    private bool wideFlow = true;
    private ProgressBar? autoProgress;
    private readonly DispatcherTimer readinessTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly StackPanel root = new() { Spacing = 18 };
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public GpuTunePanel()
    {
        Content = root;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "gpu-tune");
        DataContextChanged += (_, _) => Attach(DataContext as GpuTuneViewModel);
        SizeChanged += (_, e) => { var wide = e.NewSize.Width >= 820; if (wide != wideFlow) { wideFlow = wide; QueueRender(); } };
        readinessTimer.Tick += (_, _) => vm?.RefreshStatus();
        Loaded += (_, _) => { Subscribe(); vm?.Refresh(); readinessTimer.Start(); QueueRender(); };
        Unloaded += (_, _) => { readinessTimer.Stop(); Unsubscribe(); };
    }
    private void Attach(GpuTuneViewModel? model)
    {
        Unsubscribe();
        vm = model;
        if (IsLoaded) Subscribe();
        QueueRender();
    }
    private void Subscribe()
    {
        if (subscribed || vm is null) return;
        vm.PropertyChanged += OnChanged; vm.AutoSteps.CollectionChanged += OnStepsChanged; subscribed = true;
    }
    private void Unsubscribe()
    {
        if (!subscribed || vm is null) return;
        vm.PropertyChanged -= OnChanged; vm.AutoSteps.CollectionChanged -= OnStepsChanged; subscribed = false;
    }
    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRender();
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GpuTuneViewModel.Progress))
        {
            DispatcherQueue.TryEnqueue(() => { if (vm is null) return; if (autoProgress is not null) autoProgress.Value = vm.Progress; });
            return;
        }
        QueueRender();
    }
    private void QueueRender()
    {
        if (renderQueued || !IsLoaded) return;
        renderQueued = true;
        if (!DispatcherQueue.TryEnqueue(() => { renderQueued = false; if (IsLoaded) Render(); })) renderQueued = false;
    }

    private static Border Card(UIElement child) => new() { Style = (Style)Application.Current.Resources["CardStyle"], Child = child };
    private static TextBlock Title(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["SubtitleStyle"], TextWrapping = TextWrapping.Wrap };
    private static TextBlock Body(string text, bool muted = false) => new() { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Res("MutedBrush") : Res("MainTextBrush") };

    private void Render()
    {
        if (vm is null) return;
        var t = vm.Text;
        autoProgress = null;
        var manualExpanded = root.Children.OfType<Expander>().FirstOrDefault()?.IsExpanded ?? false;
        root.Children.Clear();

        // Intro
        var intro = new StackPanel { Spacing = 14 };
        intro.Children.Add(new TextBlock { Text = t["GtEyebrow"].ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.Bold, CharacterSpacing = 140, Foreground = Res("RoseBrush") });
        intro.Children.Add(new TextBlock { Text = t["GtHeroTitle"], FontSize = 26, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        intro.Children.Add(Body(t.Format("GtCardIntro", vm.GpuName), true));
        var activeStage = vm.Recommendation(TuneGoal.Stock) is null ? 1 : vm.TuneOnGames && vm.HasKept && vm.AppliedSetting is not null ? 4 : vm.AppliedSetting is not null ? 3 : vm.Recommendation(TuneGoal.Stock) is not null ? 2 : 1;
        var steps = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        var flowColumns = wideFlow ? 4 : 2;
        for (var col = 0; col < flowColumns; col++) steps.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < 4 / flowColumns; row++) steps.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var (key, index) in new[] { ("GtFlowTest",1), ("GtFlowCompare",2), ("GtFlowApply",3), ("GtFlowPlay",4) })
        {
            var active = index == activeStage;
            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new ColumnDefinition());
            content.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Res(active ? "AccentSoftBrush" : "CardBrush"), Child = new TextBlock { Text = index < activeStage ? "✓" : index.ToString(), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Res(active || index < activeStage ? "RoseBrush" : "MutedBrush") } });
            var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = t[key], FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (active) words.Children.Add(new TextBlock { Text = t["GtFlowCurrent"], FontSize = 11, Foreground = Res("RoseBrush") });
            Grid.SetColumn(words,1); content.Children.Add(words);
            var step = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Res(active ? "GoldGradientBrush" : "CardBorderBrush"), Background = Res(active ? "CardHoverBrush" : "CardBrush"), Child = content };
            Grid.SetColumn(step,(index-1)%flowColumns); Grid.SetRow(step,(index-1)/flowColumns); steps.Children.Add(step);
        }
        intro.Children.Add(steps);
        root.Children.Add(new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(24), BorderThickness = new Thickness(1), BorderBrush = Res("GoldGradientBrush"), Background = Res("CardBrush"), Child = intro });

        // 0. Automatic: Lotus applies through Afterburner, tests every step and puts the card back as it was at the end
        var workflow = new StackPanel { Spacing = 20 };
        var auto = new StackPanel { Spacing = 10 };
        auto.Children.Add(Title(t["GtStageTest"]));
        auto.Children.Add(Body(t["GtCompareHint"], true));
        var depth = new ComboBox { Header = t["GtDepthTitle"], HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !vm.Testing && !vm.Saving };
        foreach (var d in Enum.GetValues<GpuTestDepth>()) depth.Items.Add(new ComboBoxItem { Content = t["GtDepth" + d], Tag = d });
        depth.SelectedIndex = (int)vm.TestDepth;
        depth.SelectionChanged += (_, _) => { if (depth.SelectedItem is ComboBoxItem { Tag: GpuTestDepth d }) vm.TestDepth = d; };
        auto.Children.Add(depth);
        auto.Children.Add(Body(t["GtDepthHint" + vm.TestDepth], true));
        if (vm.MeasuredDepth is { } measuredDepth && vm.Report is not null) auto.Children.Add(Body(t.Format("GtMeasuredDepth", t["GtDepth" + measuredDepth]), true));
        var connect = new Button { Content = t["GtConnect"], IsEnabled = !vm.Testing && !vm.Saving, Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
        connect.Click += async (_, _) => { try { if (vm.OpenAfterburner is { } open) await open(); else vm.Refresh(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Connect Afterburner"); } QueueRender(); };
        var modes = new ComboBox { Header = t["GtInspectProfile"], HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !vm.Testing && !vm.Saving };
        foreach (var (goal, key) in new[] { (TuneGoal.Undervolt, "GtModeUndervolt"), (TuneGoal.Overclock, "GtModeOverclock"), (TuneGoal.Stock, "GtStock") })
            modes.Items.Add(new ComboBoxItem { Content = t[key], Tag = goal });
        modes.SelectedIndex = vm.Goal == TuneGoal.Undervolt ? 0 : vm.Goal == TuneGoal.Overclock ? 1 : 2;
        modes.SelectionChanged += (_, _) => { if (modes.SelectedItem is ComboBoxItem { Tag: TuneGoal goal }) vm.Goal = goal; };
        var autoActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var autoGo = new Button { Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 44, IsEnabled = vm.CanCompare, Content = t[vm.Report is null ? "GtCompareRun" : "GtCompareAgain"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(autoGo, "gpu-auto");
        autoGo.Click += async (_, _) => { if (await Confirm(t["GtAutoConfirmTitle"], t["GtAutoConfirmBody"])) await vm.CompareAsync(); };
        autoActions.Children.Add(autoGo);
        if (vm.Testing && vm.AutoStatus.Length > 0) { var stopAuto = new Button { Content = t["UxCancel"], Style = (Style)Application.Current.Resources["SoftButtonStyle"], MinHeight = 44 }; stopAuto.Click += (_, _) => vm.Cancel(); autoActions.Children.Add(stopAuto); }
        auto.Children.Add(autoActions);
        if (vm.Testing && vm.AutoStatus.Length > 0) { autoProgress = new ProgressBar { Minimum = 0, Maximum = 100, Value = vm.Progress, Height = 6, CornerRadius = new CornerRadius(3) }; auto.Children.Add(autoProgress); }
        if (!vm.Connected && !vm.Testing) auto.Children.Add(Body(t["GtAutoNeeds"], true));
        if (vm.AutoStatus.Length > 0) auto.Children.Add(new TextBlock { Text = vm.AutoStatus, FontWeight = FontWeights.SemiBold, Foreground = Res("GoldBrush"), TextWrapping = TextWrapping.Wrap });
        var history = new StackPanel { Spacing = 8 };
        foreach (var s in (vm.Testing ? vm.AutoSteps : vm.Report?.Steps ?? vm.AutoSteps).TakeLast(12)) history.Children.Add(Body($"{(s.Ok ? "✓" : "✕")}  {t["GtAutoMode" + s.Mode]} · {(s.Mode == "current" ? t["GtAutoAsIs"] : s.Setting.ToString())} · {t.Format("GtAutoLine", Math.Round(s.Score), Math.Round(s.MaxTempC), Math.Round(s.AvgCoreMhz), Math.Round(s.AvgPowerW))}", !s.Ok));
        workflow.Children.Add(auto);
        workflow.Children.Add(new Border { Height = 1, Background = Res("CardBorderBrush") });
        workflow.Children.Add(Title(t["GtStageChoose"]));
        workflow.Children.Add(Body(t[vm.Report is null ? "GtCardsBefore" : "GtCardsAfter"], true));

        // 1. Goal
        var goals = new Grid { ColumnSpacing = 12 };
        foreach (var (g, i) in new[] { (TuneGoal.Undervolt, 0), (TuneGoal.Overclock, 1), (TuneGoal.Stock, 2) })
        {
            goals.ColumnDefinitions.Add(new ColumnDefinition());
            var on = vm.Goal == g;
            var key = g switch { TuneGoal.Undervolt => "GtUndervolt", TuneGoal.Overclock => "GtOverclock", _ => "GtStock" };
            var p = new StackPanel { Spacing = 6 };
            p.Children.Add(new TextBlock { Text = g switch { TuneGoal.Undervolt => "🧊", TuneGoal.Overclock => "🚀", _ => "⚖️" }, FontSize = 24 });
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new TextBlock { Text = t[key], FontSize = 16, FontWeight = FontWeights.SemiBold });
            if (g == TuneGoal.Undervolt && vm.Recommendation(g) is not null) head.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Background = Res("AccentSoftBrush"), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = t["GtRecommended"], FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("RoseBrush") } });
            p.Children.Add(head);
            p.Children.Add(Body(t[key + "Hint"], true));
            if (vm.Recommendation(g) is { } result)
            {
                p.Children.Add(new Border { Height = 1, Background = Res("CardBorderBrush"), Margin = new Thickness(0,8,0,4) });
                p.Children.Add(new TextBlock { Text = g == TuneGoal.Stock ? t["GtMeasuredStock"] : result.Setting.ToString(), FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var gain = vm.Report!.Baseline!.Score > 0 ? (result.Score / vm.Report.Baseline.Score - 1) * 100 : 0;
                p.Children.Add(Body(t.Format("GtAutoSummary", gain.ToString("+0.0;-0.0;0", System.Globalization.CultureInfo.GetCultureInfo(t.Language)), Math.Round(result.MaxTempC), Math.Round(result.AvgPowerW)), true));
                if (result.Verdict == TuneVerdict.Throttled) p.Children.Add(Body(t["GtMeasuredLimited"], true));
                if (vm.AppliedSetting == result.Setting) p.Children.Add(Body(t["GtLastApplied"]));
            }
            else p.Children.Add(Body(t[vm.Report is null ? "GtNeedsMeasurement" : "GtNoApprovedResult"], true));
            var b = new Button { Content = p, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(16),
                IsEnabled = !vm.Testing && !vm.Saving && vm.Recommendation(g) is not null,
                CornerRadius = new CornerRadius(16), Background = Res(on ? "CardHoverBrush" : "CardBrush"), BorderBrush = Res(on ? "GoldGradientBrush" : "CardBorderBrush"), BorderThickness = new Thickness(on ? 2 : 1) };
            Motion.SetHover(b, 1.02); Motion.SetPress(b, true);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, "gpu-goal-" + g.ToString().ToLowerInvariant());
            b.Click += (_, _) => vm.Goal = g;
            Grid.SetColumn(b, i); goals.Children.Add(b);
        }
        var manual = new StackPanel { Spacing = 18 };
        workflow.Children.Add(goals);
        manual.Children.Add(modes);
        if (history.Children.Count > 0) manual.Children.Add(new Expander { Header = t["GtTestHistory"], Content = history, HorizontalAlignment = HorizontalAlignment.Stretch });

        // What the chosen option is for, in four short lines (purpose, what changes, who it suits, the cost)
        var gk = vm.Goal switch { TuneGoal.Undervolt => "GtUv", TuneGoal.Overclock => "GtOc", _ => "GtSt" };
        var explain = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
        explain.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); explain.ColumnDefinitions.Add(new ColumnDefinition());
        var rowIndex = 0;
        foreach (var (label, glyph) in new[] { ("Purpose", ""), ("Changes", ""), ("Suits", ""), ("Cost", "") })
        {
            explain.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
            head.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Res("RoseBrush") });
            head.Children.Add(new TextBlock { Text = t["GtLabel" + label], FontWeight = FontWeights.SemiBold, Foreground = Res("GoldBrush"), MinWidth = 120 });
            Grid.SetRow(head, rowIndex); explain.Children.Add(head);
            var body = Body(t[gk + label]); Grid.SetRow(body, rowIndex); Grid.SetColumn(body, 1); explain.Children.Add(body);
            rowIndex++;
        }
        var explainBox = new StackPanel { Spacing = 12 };
        explainBox.Children.Add(Title(t.Format("GtExplainTitle", t[vm.Goal switch { TuneGoal.Undervolt => "GtUndervolt", TuneGoal.Overclock => "GtOverclock", _ => "GtStock" }])));
        explainBox.Children.Add(explain);
        explainBox.Children.Add(Body(t["GtWhichOne"], true));
        manual.Children.Add(Card(explainBox));

        // 2. Readiness: each missing item with its one-line fix
        var ready = new StackPanel { Spacing = 10 };
        ready.Children.Add(Title(t["GtReadyTitle"]));
        ready.Children.Add(Body(t[vm.ReadinessSummaryKey], true));
        void Item(TuneReadinessItem item)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new FontIcon { Glyph = item.State switch { TuneCheckState.Ready => "", TuneCheckState.Blocked => "", _ => "" }, FontSize = 16,
                Foreground = item.State == TuneCheckState.Information ? Res("MutedBrush") : new SolidColorBrush(item.State == TuneCheckState.Ready ? Color.FromArgb(255, 0x7F, 0xD1, 0xA8) : Color.FromArgb(255, 0xF0, 0xC2, 0x7D)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
            var words = new StackPanel { Spacing = 1 }; words.Children.Add(new TextBlock { Text = t[item.LabelKey], FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (item.DetailKey is { } detail) words.Children.Add(Body(t[detail], true));
            Grid.SetColumn(words, 1); row.Children.Add(words);
            ready.Children.Add(row);
        }
        foreach (var item in vm.ReadinessItems) Item(item);
        if (vm.Goal != TuneGoal.Stock)
        {
            connect.Content = t[vm.Connected ? "GtCheckRefresh" : "GtConnect"];
            ready.Children.Add(connect);
        }
        root.Children.Add(Card(ready));

        // 3. Values and how-to
        var values = new StackPanel { Spacing = 10 };
        values.Children.Add(Title(t["GtValuesTitle"]));
        values.Children.Add(new TextBlock { Text = vm.SelectedRecommendation is { } measured ? (vm.Goal == TuneGoal.Stock ? t["GtMeasuredStock"] : measured.Setting.ToString()) : t["GtNeedsMeasurement"], FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("RoseBrush"), TextWrapping = TextWrapping.Wrap });
        if (vm.SelectedRecommendation is not null) values.Children.Add(Body(t[vm.IsRadeon ? "GtHowRadeon" : vm.Goal switch { TuneGoal.Undervolt => "GtHowUndervolt", TuneGoal.Overclock => "GtHowOverclock", _ => "GtHowStock" }]));
        manual.Children.Add(Card(values));

        if (vm.SelectedRecommendation is { } details)
        {
            values.Children.Add(Body(t.Format("GtObservedMetrics", Math.Round(details.MaxTempC), Math.Round(details.AvgCoreMhz), Math.Round(details.AvgPowerW)), true));
            values.Children.Add(Body(t["GtMeasuredScope"], true));
        }
        var manualExpander = new Expander { Header = t["GtManualTitle"], Content = manual, IsExpanded = manualExpanded, HorizontalAlignment = HorizontalAlignment.Stretch };
        // 5. Saving for games only, and 6. risks
        var save = new StackPanel { Spacing = 8 };
        save.Children.Add(Title(t["GtStageApply"]));
        save.Children.Add(Body(t["GtApplyChoiceHint"], true));
        var saveActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var saveGo = new Button { Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 44, IsEnabled = vm.CanApplySelected, Content = t["GtChangeProfile"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(saveGo, "gpu-apply-selected");
        saveGo.Click += async (_, _) => { if (vm.SelectedRecommendation is { } choice && await Confirm(t["GtAutoKeepTitle"], t.Format("GtAutoKeepBodyAuto", choice.Setting)) && vm.SelectedRecommendation?.Setting == choice.Setting) await vm.ApplySelectedAsync(); };
        saveActions.Children.Add(saveGo);
        var back = new Button { Content = t["GtReturnDefault"], IsEnabled = vm.CanReset, Style = (Style)Application.Current.Resources["SoftButtonStyle"], MinHeight = 44 };
        back.Click += async (_, _) => { if (await Confirm(t["GtReturnDefault"], t["GtResetConfirm"])) await vm.ResetAsync(); };
        saveActions.Children.Add(back);
        save.Children.Add(saveActions);
        if (vm.Saving) save.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, CornerRadius = new CornerRadius(3) });
        if (vm.SaveStatus.Length > 0) save.Children.Add(new TextBlock { Text = vm.SaveStatus, FontWeight = FontWeights.SemiBold, Foreground = Res("GoldBrush"), TextWrapping = TextWrapping.Wrap });
        save.Children.Add(Title(t["GtStageGames"]));
        save.Children.Add(Body(t["GtGamesHint"], true));
        var onGames = new ToggleSwitch { IsOn = vm.TuneOnGames, IsEnabled = vm.AppliedSetting is not null && vm.HasKept || vm.TuneOnGames, OnContent = "", OffContent = "", Header = t["GtSaveLotus"] };
        onGames.Toggled += (_, _) => vm.TuneOnGames = onGames.IsOn;
        save.Children.Add(onGames);
        if (!vm.HasKept) save.Children.Add(Body(t["GtSaveLotusNone"], true));
        workflow.Children.Add(save);
        root.Children.Add(Card(workflow));
        var profileFiles = new StackPanel { Spacing = 8 };
        profileFiles.Children.Add(Body(t["GtExportHint"], true));
        var export = new Button { Content = t["GtSaveRun"], IsEnabled = vm.CanSave && vm.AppliedSetting is not null && vm.HasKept, Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
        export.Click += async (_, _) => { if (await Confirm(t["GtSaveConfirmTitle"], t["GtSaveConfirmBody"])) { await vm.SaveAsync(); vm.AppliedSetting = null; } };
        profileFiles.Children.Add(export);
        if (vm.CanRestoreProfiles)
        {
            var restore = new Button { Content = t["GtSaveRestore"], Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
            restore.Click += async (_, _) => { await vm.RestoreProfilesAsync(); vm.AppliedSetting = null; };
            profileFiles.Children.Add(restore);
        }
        manual.Children.Add(new Expander { Header = t["GtExportTitle"], Content = profileFiles, HorizontalAlignment = HorizontalAlignment.Stretch });
        root.Children.Add(manualExpander);
        var risks = new StackPanel { Spacing = 8 };
        risks.Children.Add(Title(t["GtRisksTitle"]));
        foreach (var k in new[] { "GtRiskCrash", "GtRiskHeat", "GtRiskStartup", "GtRiskStock" }) risks.Children.Add(Body("• " + t[k]));
        root.Children.Add(Card(risks));
    }

    private async Task<bool> Confirm(string title, string body)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, RequestedTheme = ThemePalette.Element, DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, PrimaryButtonText = vm!.Text["FeatConfirm"], CloseButtonText = vm.Text["DlgCancel"] };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

