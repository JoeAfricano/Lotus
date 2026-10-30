using Lotus.Core;
using Lotus.Core.Themes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lotus.App.Controls;

// Settings › Appearance, second half: the theme gallery, "Meus temas" and shared codes.
public sealed partial class AppearancePicker
{
    private void BuildGallery(PreferencesViewModel vm)
    {
        var t = vm.Text;
        GalleryTitle.Text = t["ApGallery"]; GalleryHint.Text = t["ApGalleryHint"]; PasteLabel.Text = t["ApPaste"]; CreateLabel.Text = t["ApCreate"];
        MineTitle.Text = t["ApMine"]; MineEmpty.Text = t["ApMineEmpty"];
        var active = vm.IsCustom ? vm.CustomCode : null;
        GalleryCards.Children.Clear();
        foreach (var p in ThemeGallery.All)
            GalleryCards.Children.Add(ThemeCard(vm, p.Spec, t["ThPreset" + p.Id], active, mine: null, "theme-preset-" + p.Id));
        MineCards.Children.Clear();
        foreach (var code in vm.MyThemes)
            if (ThemeEngine.TryFromCode(code, out var s)) MineCards.Children.Add(ThemeCard(vm, s, s.Name, active, mine: code, "theme-mine"));
        MineEmpty.Visibility = vm.MyThemes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // A small picture of the theme: its background colors, a card in its surface color, the accent and the detail.
    private FrameworkElement ThemeCard(PreferencesViewModel vm, ThemeSpec spec, string name, string? active, string? mine, string id)
    {
        var t = vm.Text;
        var k = ThemeEngine.Derive(spec);
        static SolidColorBrush B(ThemeColor c, byte a = 255) => new(Color.FromArgb(a, c.R, c.G, c.B));
        var selected = active is not null && active == ThemeEngine.ToCode(spec);

        var stops = new GradientStopCollection();
        var cs = spec.Backdrop.Colors;
        for (var i = 0; i < cs.Count; i++) { var c = ThemeColor.Parse(cs[i]); stops.Add(new GradientStop { Color = Color.FromArgb(255, c.R, c.G, c.B), Offset = (double)i / Math.Max(1, cs.Count - 1) }); }
        var art = new Grid { Height = 74, CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(stops, 30) { Opacity = spec.Light ? 0.75 : 0.9 } };
        var mini = new Border
        {
            Width = 116, Height = 46, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 10), Padding = new Thickness(9, 7, 9, 7), BorderThickness = new Thickness(1),
            Background = B(k.Card, (byte)Math.Round(k.CardOpacity * 255)), BorderBrush = B(k.Border),
        };
        var lines = new StackPanel { Spacing = 5 };
        lines.Children.Add(new Border { Height = 6, Width = 70, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = B(k.Text) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new Border { Height = 12, Width = 40, CornerRadius = new CornerRadius(6), Background = B(k.AccentFill) });
        row.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = B(k.Detail) });
        lines.Children.Add(row); mini.Child = lines; art.Children.Add(mini);

        var head = new Grid { ColumnSpacing = 6 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = B(k.Text), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (selected) { var check = new FontIcon { Glyph = "", FontSize = 14, Foreground = B(k.AccentText) }; Grid.SetColumn(check, 1); head.Children.Add(check); }
        var kind = new TextBlock { Text = t["TsKind" + spec.Backdrop.Kind] + " · " + t[spec.Light ? "TsLight" : "TsDark"], FontSize = 11.5, Foreground = B(k.TextSecondary) };
        var body = new StackPanel { Spacing = 6, Padding = new Thickness(10) };
        body.Children.Add(art); body.Children.Add(head); body.Children.Add(kind);
        var surface = new Border
        {
            Child = body, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 12, 12), Background = B(k.Canvas), BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? (Brush)Application.Current.Resources["RoseBrush"] : (Brush)Application.Current.Resources["CardBorderBrush"],
        };
        var button = new Button
        {
            Content = surface, Padding = new Thickness(0), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        Motion.SetHover(button, 1.03);
        AutomationProperties.SetName(button, name + (selected ? " ✓" : ""));
        AutomationProperties.SetAutomationId(button, id);
        ToolTipService.SetToolTip(button, t["ApCardHint"]);
        button.Click += (_, _) => { if (vm.UseTheme(spec)) UiSound.Play(UiSound.Cue.Toggle); };

        // Right click: use, edit in the studio, copy the code, delete one of "Meus temas".
        var menu = new MenuFlyout();
        void Item(string label, string glyph, Action act) { var i = new MenuFlyoutItem { Text = label, Icon = new FontIcon { Glyph = glyph } }; i.Click += (_, _) => act(); menu.Items.Add(i); }
        Item(t["ApUse"], "", () => vm.UseTheme(spec));
        Item(t["ApEdit"], "", () => MainWindow.Shell?.OpenThemeStudio(mine is null ? spec with { Name = name } : spec));
        Item(t["TsCopy"], "", () => { Copy(ThemeEngine.ToCode(spec)); GalleryNote(t["ApCopied"]); });
        if (mine is not null) { menu.Items.Add(new MenuFlyoutSeparator()); Item(t["ApDelete"], "", () => vm.DeleteMyTheme(mine)); }
        button.ContextFlyout = menu;
        return button;
    }

    private static void Copy(string value)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(value);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
    }
    private void GalleryNote(string message) { GalleryMessage.Text = message; GalleryMessage.Visibility = Visibility.Visible; }
    private void OnCreate(object sender, RoutedEventArgs e) { if (MainWindow.Shell is { } shell) shell.OpenThemeStudio(shell.StudioStart()); }

    // Paste: any text with a Lotus theme code in it (a whole chat message works too). It is used and kept in "Meus temas".
    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        string? clip = null;
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) clip = await content.GetTextAsync();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        if (ThemeEngine.TryFromCode(clip, out var spec) && vm.SaveMyTheme(spec) && vm.UseTheme(spec)) { GalleryNote(vm.Text.Format("ApPasted", spec.Name)); UiSound.Play(UiSound.Cue.Done); }
        else GalleryNote(vm.Text["ApPasteNone"]);
    }
}
