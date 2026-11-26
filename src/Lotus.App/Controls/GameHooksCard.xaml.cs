using Lotus.Core.Catalog;
using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App.Controls;

// On an installed game's page, only when the game folder has a hook DLL (see GameHooks): what it is, a box explaining why it
// can cause stutter in some games, and one optional button that renames it off (or back on). Nothing changes until it is pressed.
public sealed partial class GameHooksCard : UserControl
{
    private GameDetailsViewModel? page;
    private readonly Dictionary<string, string> results = new(StringComparer.OrdinalIgnoreCase); // last outcome per file, shown under its row
    private int build;

    public GameHooksCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (page is not null) page.PropertyChanged -= OnPage;
            page = DataContext as GameDetailsViewModel;
            if (page is not null) page.PropertyChanged += OnPage;
            results.Clear(); _ = BuildAsync();
        };
        Unloaded += (_, _) => { if (page is not null) page.PropertyChanged -= OnPage; };
    }
    private void OnPage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName is nameof(GameDetailsViewModel.Installed) or null or "") DispatcherQueue.TryEnqueue(() => { results.Clear(); _ = BuildAsync(); }); }

    private async Task BuildAsync()
    {
        var mine = ++build;
        if (page?.Installed is not { } game || MainWindow.Shell is not { } shell) { Visibility = Visibility.Collapsed; return; }
        IReadOnlyList<GameHook> hooks;
        try { hooks = await Task.Run(() => shell.Hooks.Find(game)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { hooks = []; }
        if (mine != build || page?.Installed?.Key != game.Key) return;
        Rows.Children.Clear();
        foreach (var hook in hooks) Rows.Children.Add(Row(page.Text, game, hook));
        Visibility = hooks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement Row(Lotus.Core.ITextCatalog t, GameEntry game, GameHook hook)
    {
        var panel = new StackPanel { Spacing = 10 };
        var head = new Grid { ColumnSpacing = 14 };
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new()); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var badge = new Border { Style = (Style)Application.Current.Resources["IconBadgeStyle"],
            Child = new FontIcon { Glyph = hook.Disabled ? "" : "", Style = (Style)Application.Current.Resources["BadgeIconStyle"] } };
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = t.Format("HkTitle", hook.Label), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock { Text = t.Format(hook.Disabled ? "HkSummaryOff" : "HkSummaryOn", hook.Label, hook.Name), Style = (Style)Application.Current.Resources["CaptionStyle"], TextWrapping = TextWrapping.Wrap });
        // The same button in every game (José, 26/11/2026): pressed = turned off; pressing again brings the original file back.
        var button = new ToggleButton { IsChecked = hook.Disabled, VerticalAlignment = VerticalAlignment.Center, MinHeight = 36, CornerRadius = new CornerRadius(10),
            Content = t.Format("HkOff", hook.Label) };
        ToolTipService.SetToolTip(button, t[hook.Disabled ? "HkPressedHint" : "HkPressHint"]);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "game-hook-toggle");
        button.Click += async (_, _) => await Toggle(t, game, hook, button);
        Grid.SetColumn(texts, 1); Grid.SetColumn(button, 2);
        head.Children.Add(badge); head.Children.Add(texts); head.Children.Add(button);
        panel.Children.Add(head);

        // The "quadradinho": why it can stutter, and that it is optional and reversible.
        var why = new StackPanel { Spacing = 4 };
        why.Children.Add(new TextBlock { Text = t["HkWhyTitle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 });
        why.Children.Add(new TextBlock { Text = t[hook.IsScsKiller ? "HkWhyScs" : "HkWhyOther"], Style = (Style)Application.Current.Resources["CaptionStyle"], TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Border { Background = (Brush)Application.Current.Resources["SubtleBrush"], CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 12), Child = why });

        if (results.TryGetValue(hook.Name + "|" + Path.GetDirectoryName(hook.FilePath), out var said))
        {
            var status = new TextBlock { Text = said, Style = (Style)Application.Current.Resources["CaptionStyle"], TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["GoldBrush"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "game-hook-status");
            panel.Children.Add(status);
        }
        return panel;
    }

    private async Task Toggle(Lotus.Core.ITextCatalog t, GameEntry game, GameHook hook, ToggleButton button)
    {
        if (MainWindow.Shell is not { } shell) return;
        button.IsEnabled = false;
        HookChange result;
        if (shell.IsGameRunning(game)) result = HookChange.GameRunning; // the DLL is loaded; renaming it now would not take effect
        else
        {
            try { result = await Task.Run(() => hook.Disabled ? shell.Hooks.Enable(hook) : shell.Hooks.Disable(hook)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result = HookChange.NoAccess; }
        }
        results[hook.Name + "|" + Path.GetDirectoryName(hook.FilePath)] = result switch
        {
            HookChange.Done => hook.Disabled ? t.Format("HkDoneOn", hook.Label) : t["HkDoneOff"],
            HookChange.GameRunning => t["HkRunning"], HookChange.NoAccess => t["HkNoAccess"],
            HookChange.Conflict => t.Format("HkConflict", hook.Name), _ => t["HkMissing"],
        };
        UiSound.Play(result == HookChange.Done ? UiSound.Cue.Done : UiSound.Cue.Fail);
        await BuildAsync();
    }
}
