using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class PowerProfilesPanel : UserControl
{
    public PowerProfilesPanel()
    {
        InitializeComponent();
        // Loads power plans, saved profiles and journal history (read-only queries).
        Loaded += async (_, _) =>
        {
            RenderPlan();
            // The live badge ("aguardando um jogo" / "jogando X") is re-read every 3 s only while this panel is on screen.
            live ??= DispatcherQueue.CreateTimer(); live.Interval = TimeSpan.FromSeconds(3); live.Tick -= OnLive; live.Tick += OnLive; live.Start();
            if (DataContext is Lotus.Core.Optimization.PowerProfilesViewModel model) await model.RefreshAsync();
        };
        Unloaded += (_, _) => live?.Stop();
        DataContextChanged += (_, _) => Attach(DataContext as Lotus.Core.Optimization.PowerProfilesViewModel);
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? live;
    private void OnLive(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => RenderBadge();
    public event EventHandler? AdjustRequested;
    private void OnAdjustBoost(object sender, RoutedEventArgs e) => AdjustRequested?.Invoke(this, EventArgs.Empty);
    private void OnSeeScan(object sender, RoutedEventArgs e) => MainWindow.Shell?.ViewModel.NavigateCommand.Execute("analysis");

    // "O que acontece quando você joga": Perfil Jogo as a three-step timeline built from the switches the player left on;
    // Perfil Diário as normal use. Each item opens to say why and what to honestly expect.
    private void RenderPlan()
    {
        if (DataContext is not Lotus.Core.Optimization.PowerProfilesViewModel vm) return;
        var t = vm.Text;
        var game = vm.IsGame;
        ChangesTitle.Text = t[game ? "PfPlanGameTitle" : "PfPlanDailyTitle"];
        ChangesSubtitle.Text = t[game ? "PfPlanGameSubtitle" : "PfPlanDailySubtitle"];
        DecideBody.Text = t[game ? "PfDecideGame" : "PfDecideDaily"];
        ApplyLabel.Text = t[game ? "PfActivateGame" : "PfActivateDaily"];
        AdjustLabel.Text = t["BoTitle"];
        AdjustButton.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
        PowerRow.Visibility = game ? Visibility.Collapsed : Visibility.Visible;
        ScanLink.Content = t["PfSeeScan"];
        GameTimeline.Children.Clear();
        if (game)
        {
            var s = vm.BoostState?.Invoke() ?? (true, true, true, true, true);
            GameTimeline.Children.Add(Stage("1", t["PfStageOpen"]));
            if (s.Power) GameTimeline.Children.Add(Item("", t["BoPower"], t["PfWhyPower"]));
            if (s.Notifications) GameTimeline.Children.Add(Item("", t["BoNotifications"], t["PfWhyNotifications"]));
            if (s.Recording) GameTimeline.Children.Add(Item("", t["BoRecording"], t["PfWhyRecording"]));
            if (s.Priority) GameTimeline.Children.Add(Item("", t["PfPriorityLine"], t["PfWhyPriority"]));
            if (!(s.Power || s.Notifications || s.Recording || s.Priority)) GameTimeline.Children.Add(Item("", t["PfNothingOn"], t["PfNothingOnWhy"]));
            GameTimeline.Children.Add(Stage("2", t["PfStagePlaying"]));
            GameTimeline.Children.Add(Item("", t["PfPlayingLine"], t["PfWhyPlaying"]));
            GameTimeline.Children.Add(Stage("3", t["PfStageClose"]));
            GameTimeline.Children.Add(Item("", t["PfRestoreLine"], t["PfWhyRestore"]));
        }
        else GameTimeline.Children.Add(Item("", t["PfDailyLine"], t["PfWhyDaily"]));
        RenderBadge();
    }
    private static FrameworkElement Stage(string number, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentGradientBrush"],
            Child = new TextBlock { Text = number, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["OnRoseBrush"] } });
        row.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    private static FrameworkElement Item(string glyph, string title, string why)
    {
        var head = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 4, 0, 4) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new Grid { Width = 30, Height = 30 };
        icon.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentSoftBrush"] });
        icon.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["RoseBrush"] });
        head.Children.Add(icon);
        var label = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); head.Children.Add(label);
        return new Expander { Header = head, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(36, 0, 0, 0),
            Content = new TextBlock { Text = why, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionStyle"], FontSize = 13 } };
    }
    // "Ativo · aguardando um jogo" / "Ativo · jogando X" while the Game profile is on.
    private void RenderBadge()
    {
        if (DataContext is not Lotus.Core.Optimization.PowerProfilesViewModel vm) return;
        var s = vm.BoostState?.Invoke();
        if (s is not { On: true }) { ActiveBadge.Visibility = Visibility.Collapsed; return; }
        var playing = vm.PlayingGame?.Invoke();
        var c = playing is null ? Windows.UI.Color.FromArgb(255, 0xF0, 0xC2, 0x7D) : Windows.UI.Color.FromArgb(255, 0x7F, 0xD1, 0xA8);
        ActiveText.Text = playing is null ? vm.Text["PfActiveWaiting"] : vm.Text.Format("PfActivePlaying", playing);
        ActiveText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
        ActiveDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
        ActiveBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x24, c.R, c.G, c.B));
        ActiveBadge.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x59, c.R, c.G, c.B));
        ActiveBadge.Visibility = Visibility.Visible;
    }

    // Visual confirmation: the chosen card's check springs when the profile changes; applying/undoing
    // shows the result card with a badge pop and a ring ripple (skipped when animations are off).
    private Lotus.Core.Optimization.PowerProfilesViewModel? attached;
    private void Attach(Lotus.Core.Optimization.PowerProfilesViewModel? vm)
    {
        if (ReferenceEquals(vm, attached)) return;
        if (attached is not null) { attached.ResultShown -= OnResultShown; attached.PropertyChanged -= OnVmChanged; }
        attached = vm;
        if (vm is not null) { vm.ResultShown += OnResultShown; vm.PropertyChanged += OnVmChanged; }
    }
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // IsCustom is the last of the three notifications a switch raises: react once per switch.
        if (sender is not Lotus.Core.Optimization.PowerProfilesViewModel vm || e.PropertyName != "IsCustom") return;
        var check = vm.IsGame ? GameCheck : vm.IsDaily ? DailyCheck : CustomCheck;
        DispatcherQueue.TryEnqueue(() =>
        {
            RenderPlan();
            // Neutral hint style (the success/warning look belongs to a finished apply or undo).
            ResultIcon.Glyph = ""; ResultIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["RoseBrush"];
            ResultBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentSoftBrush"];
            ResultCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerBrush"];
            Motion.Pop(check); Motion.Settle(ResultCard);
        });
    }
    private void OnResultShown(object? sender, EventArgs e)
    {
        if (sender is not Lotus.Core.Optimization.PowerProfilesViewModel vm) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            var brush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[vm.ResultOk ? "SuccessBrush" : "WarningBrush"];
            ResultIcon.Glyph = vm.ResultOk ? "" : ""; ResultIcon.Foreground = brush; ResultRing.Stroke = brush;
            ResultBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(((Microsoft.UI.Xaml.Media.SolidColorBrush)brush).Color) { Opacity = 0.18 };
            ResultCard.BorderBrush = brush;
            Motion.Settle(ResultCard); Motion.Pop(ResultBadge);
            if (!Motion.Active) return;
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            void Anim(DependencyObject target, string prop, double from, double to)
            {
                var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, target); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, prop); sb.Children.Add(a);
            }
            Anim(ResultRingScale, "ScaleX", 1, 2.1); Anim(ResultRingScale, "ScaleY", 1, 2.1); Anim(ResultRing, "Opacity", 0.8, 0);
            sb.Begin();
        });
    }

    // Opens only Windows Settings pages (ms-settings:); nothing is changed by Lotus.
    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string uri } && uri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }

    // One click: the profile is analyzed, the full list of changes is shown once, and confirming applies everything.
    private async void OnApplyProfile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Lotus.Core.Optimization.PowerProfilesViewModel vm) return;
        if (vm.IsGame) { vm.ArmGameSession(); RenderPlan(); return; } // Perfil Jogo: only during games, nothing to change now
        var changes = await vm.PreviewProfileAsync();
        var pending = changes.Where(c => !c.AlreadyApplied).ToList();
        if (pending.Count == 0 && vm.PreviewFailed) { vm.ReportPreviewFailed(); return; }
        if (pending.Count == 0) { await vm.ApplyProfileAsync(changes); vm.ProfileApplied?.Invoke(vm.IsGame ? "game" : vm.IsDaily ? "daily" : "custom"); return; }
        var list = new StackPanel { Spacing = 8, MaxWidth = 460 };
        list.Children.Add(new TextBlock { Text = vm.Text["PfPreviewIntro"], TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var c in changes)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new FontIcon { Glyph = c.AlreadyApplied ? "" : "", FontSize = 13, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
            var text = new StackPanel(); Grid.SetColumn(text, 1);
            text.Children.Add(new TextBlock { Text = c.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = c.AlreadyApplied ? $"{c.To} · {vm.Text["PfAlready"]}" : $"{c.From}  →  {c.To}", Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text); list.Children.Add(row);
        }
        if (vm.PreviewSkipped > 0 || vm.PreviewFailed) list.Children.Add(new TextBlock { Text = vm.Text["PfPreviewSkipped"], TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GoldBrush"], Margin = new Thickness(0, 6, 0, 0) });
        list.Children.Add(new TextBlock { Text = vm.Text["PowerRisk"], TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 6, 0, 0) });
        if (await ShowAsync(vm.Text.Format("PfPreviewTitle", vm.Text[ProfileTitleKey(vm)]), list, vm.Text["PfApplyConfirm"], vm.Text))
        { await vm.ApplyProfileAsync(changes); vm.ProfileApplied?.Invoke(vm.IsGame ? "game" : vm.IsDaily ? "daily" : "custom"); }
    }
    private static string ProfileTitleKey(Lotus.Core.Optimization.PowerProfilesViewModel vm) => vm.IsGame ? "PfGameTitle" : vm.IsDaily ? "PfDailyTitle" : "PfCustomTitle";
    private async void OnUndoProfile(object sender, RoutedEventArgs e)
    {
        if (DataContext is Lotus.Core.Optimization.PowerProfilesViewModel vm && await ConfirmAsync(vm.Text["PfUndoTitle"], vm.Text["PfUndoBody"], vm.Text))
            await vm.UndoProfileAsync();
    }
    // Undo of a specific history entry: the dialog replaces the old confirmation checkbox.
    private async void OnUndoOperation(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Lotus.Core.Optimization.PowerProfilesViewModel vm || !await ConfirmAsync(vm.Text["PfUndoTitle"], vm.Text["PfUndoBody"], vm.Text)) return;
        vm.Confirmed = true;
        if (vm.RevertCommand.CanExecute(null)) await vm.RevertCommand.ExecuteAsync(null); else vm.Confirmed = false;
    }

    private async void OnToggleStartup(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Lotus.Core.Optimization.StartupRow row } || DataContext is not Lotus.Core.Optimization.PowerProfilesViewModel vm) return;
        if (await ConfirmAsync(vm.Text.Format("StartupConfirmTitle", row.Name, row.ToggleLabel ?? ""), vm.Text["StartupConfirmBody"], vm.Text))
            await vm.ToggleStartupAsync(row);
    }

    // Real feature actions always pass through an explicit confirmation dialog.
    private async void OnApplyFeature(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && DataContext is Lotus.Core.Optimization.PowerProfilesViewModel vm
            && await ConfirmAsync(vm.Text.Format("FeatConfirmTitle", vm.Text["FeatName" + id]), vm.Text["FeatConfirmBody"], vm.Text))
            await vm.ApplyFeatureAsync(id, confirmed: true);
    }
    private async void OnUndoFeature(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && DataContext is Lotus.Core.Optimization.PowerProfilesViewModel vm
            && await ConfirmAsync(vm.Text["FeatConfirmUndoTitle"], vm.Text["FeatConfirmUndoBody"], vm.Text))
            await vm.UndoFeatureAsync(id, confirmed: true);
    }
    private Task<bool> ConfirmAsync(string title, string body, Lotus.Core.ITextCatalog text) =>
        ShowAsync(title, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, text["FeatConfirm"], text, primaryDefault: false);
    private async Task<bool> ShowAsync(string title, UIElement content, string primary, Lotus.Core.ITextCatalog text, bool primaryDefault = true)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = text["DlgCancel"],
            DefaultButton = primaryDefault ? ContentDialogButton.Primary : ContentDialogButton.Close, RequestedTheme = ThemePalette.Element
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Three columns on wide windows, one stacked column on narrow ones.
    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Two profiles side by side (Personalizado is hidden: "Ajustar o perfil Jogo" replaced it); stacked when narrow.
        var stacked = e.NewSize.Width < 680;
        CustomCard.Visibility = Visibility.Collapsed;
        ProfileCards.ColumnDefinitions[2].Width = new GridLength(0);
        foreach (var (card, index) in new FrameworkElement[] { GameCard, DailyCard }.Select((c, i) => (c, i)))
        {
            Grid.SetColumn(card, stacked ? 0 : index); Grid.SetRow(card, stacked ? index : 0); Grid.SetColumnSpan(card, stacked ? 2 : 1);
        }
        ProfileCards.ColumnSpacing = 16;
    }

    // Actions move below the change list when there is no room for a side column.
    private void OnChangesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 700;
        ChangesGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(260);
        Grid.SetColumn(ActionsColumn, stacked ? 0 : 1); Grid.SetRow(ActionsColumn, stacked ? 1 : 0);
    }
}
