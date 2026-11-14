using System.ComponentModel;
using Lotus.Core.Telemetry;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lotus.App.Controls;

// Otimizar › "Ajuste da placa": pick a goal, check what is ready, read the values to type in MSI Afterburner, test them
// (on request), save them for games only, and know the risks. Lotus never changes clocks or Afterburner itself.
public sealed class GpuTunePanel : UserControl
{
    private GpuTuneViewModel? vm;
    private readonly StackPanel root = new() { Spacing = 18 };
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public GpuTunePanel()
    {
        Content = root;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "gpu-tune");
        DataContextChanged += (_, _) => Attach(DataContext as GpuTuneViewModel);
        Loaded += (_, _) => { vm?.Refresh(); Render(); };
        Unloaded += (_, _) => { if (vm is not null) vm.PropertyChanged -= OnChanged; };
    }
    private void Attach(GpuTuneViewModel? model)
    {
        if (vm is not null) vm.PropertyChanged -= OnChanged;
        vm = model;
        if (vm is not null) { vm.PropertyChanged += OnChanged; vm.AutoSteps.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(Render); }
        Render();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(Render);

    private static Border Card(UIElement child) => new() { Style = (Style)Application.Current.Resources["CardStyle"], Child = child };
    private static TextBlock Title(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["SubtitleStyle"], TextWrapping = TextWrapping.Wrap };
    private static TextBlock Body(string text, bool muted = false) => new() { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Res("MutedBrush") : Res("MainTextBrush") };

    private void Render()
    {
        if (vm is null) return;
        var t = vm.Text;
        root.Children.Clear();

        // Intro
        var intro = new StackPanel { Spacing = 4 };
        intro.Children.Add(new TextBlock { Text = t["GtEyebrow"].ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.Bold, CharacterSpacing = 140, Foreground = Res("RoseBrush") });
        intro.Children.Add(Body(t.Format("GtCardIntro", vm.GpuName), true));
        root.Children.Add(intro);

        // 0. Automatic: Lotus applies through Afterburner, tests every step and puts the card back as it was at the end
        var auto = new StackPanel { Spacing = 10 };
        auto.Children.Add(Title(t["GtAutoTitle"]));
        auto.Children.Add(Body(t["GtAutoHint"], true));
        var autoActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var autoGo = new Button { Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 44, IsEnabled = vm.CanAuto, Content = t["GtAutoRun"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(autoGo, "gpu-auto");
        autoGo.Click += async (_, _) => { if (await Confirm(t["GtAutoConfirmTitle"], t["GtAutoConfirmBody"])) await vm.AutoAsync(AutoModes.All); };
        autoActions.Children.Add(autoGo);
        if (vm.Testing && vm.AutoStatus.Length > 0) { var stopAuto = new Button { Content = t["UxCancel"], Style = (Style)Application.Current.Resources["SoftButtonStyle"], MinHeight = 44 }; stopAuto.Click += (_, _) => vm.Cancel(); autoActions.Children.Add(stopAuto); }
        auto.Children.Add(autoActions);
        if (!vm.CanAuto && !vm.Testing) auto.Children.Add(Body(t["GtAutoNeeds"], true));
        if (vm.AutoStatus.Length > 0) auto.Children.Add(new TextBlock { Text = vm.AutoStatus, FontWeight = FontWeights.SemiBold, Foreground = Res("GoldBrush"), TextWrapping = TextWrapping.Wrap });
        foreach (var s in vm.AutoSteps.TakeLast(12)) auto.Children.Add(Body($"{(s.Ok ? "✓" : "✕")}  {t["GtAutoMode" + s.Mode]} · {(s.Mode == "current" ? t["GtAutoAsIs"] : s.Setting.ToString())} · {t.Format("GtAutoLine", Math.Round(s.Score), Math.Round(s.MaxTempC), Math.Round(s.AvgCoreMhz), Math.Round(s.AvgPowerW))}", !s.Ok));
        if (vm.Report is { Baseline: { } baseStep } rep)
        {
            auto.Children.Add(new TextBlock { Text = t["GtAutoResults"], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            foreach (var (key, step) in new[] { ("current", rep.Current), ("baseline", (AutoStep?)baseStep), ("undervolt", rep.Undervolt), ("overclock", rep.Overclock), ("combined", rep.Combined) })
            {
                if (step is null) { auto.Children.Add(Body(t["GtAutoMode" + key] + " · " + t["GtAutoNone"], true)); continue; }
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var gain = baseStep.Score > 0 ? (step.Score / baseStep.Score - 1) * 100 : 0;
                row.Children.Add(Body($"{t["GtAutoMode" + key]} · {(key == "current" ? t["GtAutoAsIs"] : step.Setting.ToString())} · {t.Format("GtAutoSummary", gain.ToString("+0.0;-0.0;0", System.Globalization.CultureInfo.GetCultureInfo(t.Language)), Math.Round(step.MaxTempC), Math.Round(step.AvgPowerW))}"));
                if (key is not ("baseline" or "current") && vm.ApplySetting is not null)
                {
                    var keep = new Button { Content = t["GtAutoKeep"], Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
                    var chosen = step.Setting;
                    keep.Click += async (_, _) => { if (await Confirm(t["GtAutoKeepTitle"], t.Format("GtAutoKeepBodyAuto", chosen))) vm.Keep(chosen); };
                    Grid.SetColumn(keep, 1); row.Children.Add(keep);
                }
                auto.Children.Add(row);
            }
        }
        root.Children.Add(Card(auto));

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
            if (g == TuneGoal.Undervolt) head.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Background = Res("AccentSoftBrush"), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = t["GtRecommended"], FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("RoseBrush") } });
            p.Children.Add(head);
            p.Children.Add(Body(t[key + "Hint"], true));
            var b = new Button { Content = p, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(16),
                CornerRadius = new CornerRadius(16), Background = Res(on ? "CardHoverBrush" : "CardBrush"), BorderBrush = Res(on ? "GoldGradientBrush" : "CardBorderBrush"), BorderThickness = new Thickness(on ? 2 : 1) };
            Motion.SetHover(b, 1.02); Motion.SetPress(b, true);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, "gpu-goal-" + g.ToString().ToLowerInvariant());
            b.Click += (_, _) => vm.Goal = g;
            Grid.SetColumn(b, i); goals.Children.Add(b);
        }
        root.Children.Add(goals);

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
        root.Children.Add(Card(explainBox));

        // 2. Readiness: each missing item with its one-line fix
        var ready = new StackPanel { Spacing = 10 };
        ready.Children.Add(Title(t["GtReadyTitle"]));
        void Item(bool ok, string label, string fix)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new FontIcon { Glyph = ok ? "" : "", FontSize = 16, Foreground = new SolidColorBrush(ok ? Color.FromArgb(255, 0x7F, 0xD1, 0xA8) : Color.FromArgb(255, 0xF0, 0xC2, 0x7D)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
            var words = new StackPanel { Spacing = 1 }; words.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!ok) words.Children.Add(Body(fix, true));
            Grid.SetColumn(words, 1); row.Children.Add(words);
            ready.Children.Add(row);
        }
        var r = vm.Ready;
        Item(r.Nvidia, t["GtReadyNvidia"], t["GtFixNvidia"]);
        Item(r.Afterburner, t["GtReadyAfterburner"], t["GtFixAfterburner"]);
        Item(r.RivaTuner, t["GtReadyRivaTuner"], t["GtFixRivaTuner"]);
        Item(r.VoltageUnlocked, t["GtReadyVoltage"], t["GtFixVoltage"]);
        Item(r.AutoProfiles, t["GtReadyAuto"], t["GtFixAuto"]);
        if (r.Afterburner)
        {
            var open = new Button { Content = t["GtOpenAfterburner"], Style = (Style)Application.Current.Resources["SoftButtonStyle"], HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Lotus.Infrastructure.Integrations.AfterburnerSetup.Exe) { UseShellExecute = true })?.Dispose(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Open Afterburner"); } };
            ready.Children.Add(open);
        }
        root.Children.Add(Card(ready));

        // 3. Values and how-to
        var values = new StackPanel { Spacing = 10 };
        values.Children.Add(Title(t["GtValuesTitle"]));
        values.Children.Add(new TextBlock { Text = vm.Values, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("RoseBrush"), TextWrapping = TextWrapping.Wrap });
        values.Children.Add(Body(t[vm.IsRadeon ? "GtHowRadeon" : vm.Goal switch { TuneGoal.Undervolt => "GtHowUndervolt", TuneGoal.Overclock => "GtHowOverclock", _ => "GtHowStock" }]));
        root.Children.Add(Card(values));

        // 4. Stability test
        if (vm.Goal != TuneGoal.Stock)
        {
            var test = new StackPanel { Spacing = 10 };
            test.Children.Add(Title(t["GtTestTitle"]));
            test.Children.Add(Body(t["GtTestHint"], true));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var go = new Button { Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 44, IsEnabled = vm.CanTest, Content = t["GtTestRun"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(go, "gpu-test");
            go.Click += async (_, _) => { if (await Confirm(t["GtTestConfirmTitle"], t.Format("GtTestConfirmBody", vm.Values))) await vm.TestAsync(); };
            actions.Children.Add(go);
            if (vm.Testing) { var stop = new Button { Content = t["UxCancel"], Style = (Style)Application.Current.Resources["SoftButtonStyle"], MinHeight = 44 }; stop.Click += (_, _) => vm.Cancel(); actions.Children.Add(stop); }
            test.Children.Add(actions);
            if (vm.Testing) test.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = vm.Progress, Height = 6, CornerRadius = new CornerRadius(3) });
            if (vm.Verdict.Length > 0) test.Children.Add(new TextBlock { Text = vm.Verdict, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Res("GoldBrush") });
            root.Children.Add(Card(test));
        }

        // 5. Saving for games only, and 6. risks
        var save = new StackPanel { Spacing = 8 };
        save.Children.Add(Title(t["GtSaveTitle"]));
        save.Children.Add(Body(t["GtSaveBodyAuto"]));
        var saveActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var saveGo = new Button { Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 44, IsEnabled = vm.CanSave, Content = t["GtSaveRun"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(saveGo, "gpu-save-profiles");
        saveGo.Click += async (_, _) => { if (await Confirm(t["GtSaveConfirmTitle"], t["GtSaveConfirmBody"])) await vm.SaveAsync(); };
        saveActions.Children.Add(saveGo);
        if (vm.CanRestoreProfiles)
        {
            var back = new Button { Content = t["GtSaveRestore"], Style = (Style)Application.Current.Resources["SoftButtonStyle"], MinHeight = 44 };
            back.Click += async (_, _) => await vm.RestoreProfilesAsync();
            saveActions.Children.Add(back);
        }
        save.Children.Add(saveActions);
        if (vm.Saving) save.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, CornerRadius = new CornerRadius(3) });
        if (vm.SaveStatus.Length > 0) save.Children.Add(new TextBlock { Text = vm.SaveStatus, FontWeight = FontWeights.SemiBold, Foreground = Res("GoldBrush"), TextWrapping = TextWrapping.Wrap });
        var onGames = new ToggleSwitch { IsOn = vm.TuneOnGames, IsEnabled = vm.HasKept || vm.TuneOnGames, OnContent = "", OffContent = "", Header = t["GtSaveLotus"] };
        onGames.Toggled += (_, _) => vm.TuneOnGames = onGames.IsOn;
        save.Children.Add(onGames);
        if (!vm.HasKept) save.Children.Add(Body(t["GtSaveLotusNone"], true));
        root.Children.Add(Card(save));
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

