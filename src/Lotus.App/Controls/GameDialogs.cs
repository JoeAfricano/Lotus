using Lotus.Core;
using Lotus.Core.Catalog;
using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace Lotus.App.Controls;

// "Adicionar jogo" and "Editar nome e capa": find the game in the Steam store (name and cover come along) or fill it in
// by hand, with an image of your own and, optionally, the .exe that starts it.
public static class GameDialogs
{
    private sealed class Form
    {
        public string? Cover, SteamAppId, Exe;
        public readonly TextBox Name = new() { MinWidth = 300 };
        public readonly Border Preview = new() { Width = 132, Height = 198, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        public readonly TextBlock Initials = new() { FontFamily = new FontFamily("Georgia"), FontSize = 36, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        public void ShowCover()
        {
            var source = Cover is { Length: > 0 } c && Uri.TryCreate(c, UriKind.Absolute, out var u) ? new BitmapImage(u) { DecodePixelWidth = 264 } : null;
            Preview.Background = source is null ? (Brush)Application.Current.Resources["HeroBrush"] : new ImageBrush { ImageSource = source, Stretch = Stretch.UniformToFill };
            Initials.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
            Initials.Text = string.Concat(Name.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    private static TextBlock Caption(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12.5 };

    // The store search: typing shows matching games with their art; picking one fills the name and the exact cover.
    private static UIElement StoreSearch(ITextCatalog t, Form form, Action changed)
    {
        var panel = new StackPanel { Spacing = 8 };
        var box = new AutoSuggestBox { PlaceholderText = t["AgSearch"], QueryIcon = new SymbolIcon(Symbol.Find) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "addgame-search");
        var results = new ListView { MaxHeight = 220, SelectionMode = ListViewSelectionMode.Single };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(results, "addgame-results");
        var status = Caption("");
        CancellationTokenSource? cts = null;
        box.TextChanged += async (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            cts?.Cancel(); var mine = cts = new CancellationTokenSource();
            var q = box.Text.Trim();
            if (q.Length < 2) { results.Items.Clear(); status.Text = ""; return; }
            try
            {
                await Task.Delay(350, mine.Token);
                status.Text = t["AgSearching"];
                var found = MainWindow.Shell is { } shell ? await shell.Catalog.SearchAsync(q, mine.Token) : [];
                if (mine.IsCancellationRequested) return;
                results.Items.Clear();
                foreach (var item in found.Take(12))
                {
                    var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4), Tag = item };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new Border
                    {
                        Width = 92, Height = 43, CornerRadius = new CornerRadius(6),
                        Background = new ImageBrush { ImageSource = new BitmapImage(new Uri(item.Wide)) { DecodePixelWidth = 184 }, Stretch = Stretch.UniformToFill },
                    });
                    var name = new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(name, 1); row.Children.Add(name);
                    results.Items.Add(new ListViewItem { Content = row, Tag = item });
                }
                status.Text = found.Count == 0 ? t["AgNothing"] : "";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Add game search"); status.Text = t["AgOffline"]; }
        };
        results.SelectionChanged += async (_, _) =>
        {
            if (results.SelectedItem is not ListViewItem { Tag: CatalogItem item }) return;
            form.Name.Text = item.Name; form.SteamAppId = item.AppId;
            form.Cover = CatalogText.Portrait(item.AppId); form.ShowCover(); changed();
            try
            {
                if (MainWindow.Shell is { } shell && await shell.Catalog.PortraitAsync(item.AppId, CancellationToken.None) is { } exact && form.SteamAppId == item.AppId)
                { form.Cover = exact; form.ShowCover(); changed(); }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Add game cover"); }
        };
        panel.Children.Add(box); panel.Children.Add(status); panel.Children.Add(results);
        return panel;
    }

    private static async Task<string?> PickFile(params string[] types)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = types.Contains(".exe") ? PickerViewMode.List : PickerViewMode.Thumbnail };
        if (types.Contains(".exe")) picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        foreach (var type in types) picker.FileTypeFilter.Add(type);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private static Grid Layout(ITextCatalog t, Form form, Action changed, bool withExe, UIElement? extraButton)
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        form.Preview.BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"];
        form.Preview.Child = form.Initials;
        grid.Children.Add(form.Preview);
        var right = new StackPanel { Spacing = 10 };
        form.Name.Header = t["AgName"];
        form.Name.TextChanged += (_, _) => { if (form.Cover is null) form.ShowCover(); changed(); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(form.Name, "addgame-name");
        right.Children.Add(form.Name);
        var coverRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var pickCover = new Button { Content = t["AgPickCover"], Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
        pickCover.Click += async (_, _) => { if (await PickFile(".png", ".jpg", ".jpeg", ".webp", ".bmp") is { } file) { form.Cover = file; form.ShowCover(); changed(); } };
        coverRow.Children.Add(pickCover);
        if (extraButton is not null) coverRow.Children.Add(extraButton);
        right.Children.Add(coverRow);
        if (withExe)
        {
            var exeText = Caption(t["AgNoExe"]);
            var pickExe = new Button { Content = t["AgPickExe"], Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(pickExe, "addgame-exe");
            pickExe.Click += async (_, _) =>
            {
                if (await PickFile(".exe") is not { } exe) return;
                form.Exe = exe; exeText.Text = exe;
                if (string.IsNullOrWhiteSpace(form.Name.Text)) form.Name.Text = Path.GetFileNameWithoutExtension(exe);
                changed();
            };
            right.Children.Add(new TextBlock { Text = t["AgExeTitle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            right.Children.Add(pickExe); right.Children.Add(exeText);
            right.Children.Add(Caption(t["AgExeHint"]));
        }
        Grid.SetColumn(right, 1); grid.Children.Add(right);
        return grid;
    }

    public static async Task<GameEntry?> AddAsync(XamlRoot root, GamesViewModel vm)
    {
        var t = vm.Text;
        var form = new Form();
        ContentDialog? dialog = null;
        void Changed() { if (dialog is not null) dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(form.Name.Text) || form.Exe is not null; }
        var body = new StackPanel { Spacing = 16, Width = 600, Margin = new Thickness(0, 0, 16, 0) };
        body.Children.Add(Caption(t["AgIntro"]));
        body.Children.Add(StoreSearch(t, form, Changed));
        body.Children.Add(Layout(t, form, Changed, withExe: true, null));
        form.ShowCover();
        dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = ThemePalette.Element, Title = t["AgTitle"], Content = new ScrollViewer { Content = body, MaxHeight = 620 },
            PrimaryButtonText = t["AgAdd"], CloseButtonText = t["DlgCancel"], DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var added = await vm.AddCustomAsync(form.Name.Text, form.Exe, form.Cover, form.SteamAppId);
        UiSound.Play(added is null ? UiSound.Cue.Fail : UiSound.Cue.Done);
        return added;
    }

    public static async Task<bool> EditAsync(XamlRoot root, GamesViewModel vm, GameEntry game)
    {
        var t = vm.Text;
        var form = new Form { Cover = game.CoverPath };
        form.Name.Text = game.Name;
        var reset = false; string? newCover = null;
        var original = new Button { Content = t["AgOriginalCover"], Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
        original.Click += (_, _) => { reset = true; newCover = null; form.Cover = vm.OriginalCover(game); form.ShowCover(); };
        void Changed() { if (form.Cover != game.CoverPath && form.Cover != vm.OriginalCover(game)) { newCover = form.Cover; reset = false; } }
        var body = new StackPanel { Spacing = 16, Width = 600, Margin = new Thickness(0, 0, 16, 0) };
        body.Children.Add(Caption(t.Format("AgEditIntro", vm.OriginalName(game))));
        body.Children.Add(StoreSearch(t, form, Changed));
        body.Children.Add(Layout(t, form, Changed, withExe: false, original));
        form.ShowCover();
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = ThemePalette.Element, Title = t["AgEditTitle"], Content = new ScrollViewer { Content = body, MaxHeight = 620 },
            PrimaryButtonText = t["AgSave"], CloseButtonText = t["DlgCancel"], DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
        var ok = await vm.CustomizeAsync(game, form.Name.Text, newCover, reset);
        UiSound.Play(ok ? UiSound.Cue.Done : UiSound.Cue.Fail);
        return ok;
    }

    public static async Task<bool> ConfirmRemoveAsync(XamlRoot root, ITextCatalog t, GameEntry game)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = ThemePalette.Element, Title = t.Format("AgRemoveTitle", game.Name),
            Content = new TextBlock { Text = t["AgRemoveBody"], TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = t["AgRemove"], CloseButtonText = t["DlgCancel"], DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
