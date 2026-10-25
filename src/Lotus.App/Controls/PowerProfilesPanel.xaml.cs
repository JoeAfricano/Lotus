using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class PowerProfilesPanel : UserControl
{
    public PowerProfilesPanel()
    {
        InitializeComponent();
        // Loads power plans, saved profiles and journal history (read-only queries).
        Loaded += async (_, _) => { if (DataContext is Lotus.Core.Optimization.PowerProfilesViewModel model) await model.RefreshAsync(); };
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
        var changes = await vm.PreviewProfileAsync();
        var pending = changes.Where(c => !c.AlreadyApplied).ToList();
        if (pending.Count == 0) { await vm.ApplyProfileAsync(changes); return; }
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
        list.Children.Add(new TextBlock { Text = vm.Text["PowerRisk"], TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 6, 0, 0) });
        if (await ShowAsync(vm.Text.Format("PfPreviewTitle", vm.Text[ProfileTitleKey(vm)]), list, vm.Text["PfApplyConfirm"], vm.Text))
            await vm.ApplyProfileAsync(changes);
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
            DefaultButton = primaryDefault ? ContentDialogButton.Primary : ContentDialogButton.Close, RequestedTheme = ElementTheme.Dark
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Three columns on wide windows, one stacked column on narrow ones.
    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 820;
        foreach (var (card, index) in new FrameworkElement[] { GameCard, DailyCard, CustomCard }.Select((c, i) => (c, i)))
        {
            Grid.SetColumn(card, stacked ? 0 : index); Grid.SetRow(card, stacked ? index : 0); Grid.SetColumnSpan(card, stacked ? 3 : 1);
        }
    }

    // Actions move below the change list when there is no room for a side column.
    private void OnChangesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 700;
        ChangesGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(260);
        Grid.SetColumn(ActionsColumn, stacked ? 0 : 1); Grid.SetRow(ActionsColumn, stacked ? 1 : 0);
    }
}
