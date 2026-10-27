using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lotus.App.Controls;

// Settings › Appearance: mode cards (each a little preview of that mode) and accent dots. Applied live and saved;
// the selected card/dot shows a check mark, so the choice never depends on color alone.
public sealed partial class AppearancePicker : UserControl
{
    private PreferencesViewModel? Vm => DataContext as PreferencesViewModel;
    private static readonly (string Id, string Label, string Fill)[] AccentList =
        [("rose", "AccentRose", "#AD245E"), ("violet", "AccentViolet", "#6D43B5"), ("blue", "AccentBlue", "#245CC1"), ("green", "AccentGreen", "#28715A"),
         ("crimson", "AccentCrimson", "#B02236"), ("gold", "AccentGold", "#8F5E16"), ("dark", "AccentDark", "#3E3239")];
    // Preview colors per mode: canvas, card, text, sample accent bar.
    private static readonly Dictionary<string, (string Bg, string Card, string Text)> Previews = new()
    {
        ["Midnight"] = ("#100D15", "#211A29", "#F8F5FA"), ["Light"] = ("#F5F1F6", "#FFFFFF", "#281C30"),
        ["System"] = ("#2D2336", "#211A29", "#F8F5FA"), ["Graphite"] = ("#131316", "#222228", "#F5F5F7"),
    };

    public AppearancePicker()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (Vm is { } vm) { vm.PropertyChanged -= OnVm; vm.PropertyChanged += OnVm; Build(); } };
        Unloaded += (_, _) => { if (Vm is { } vm) vm.PropertyChanged -= OnVm; };
    }
    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreferencesViewModel.ThemeIndex) or nameof(PreferencesViewModel.Accent) or nameof(PreferencesViewModel.ThemeOptions)) DispatcherQueue.TryEnqueue(Build);
    }

    private void Build()
    {
        if (Vm is not { } vm) return;
        var t = vm.Text;
        Title.Text = t["ApThemeTitle"]; AccentTitle.Text = t["ApAccent"];
        var fill = AccentList.FirstOrDefault(a => a.Id == vm.Accent).Fill ?? "#AD245E";
        ThemeCards.Items.Clear();
        for (var i = 0; i < InterfacePreferences.Themes.Length; i++)
        {
            var id = InterfacePreferences.Themes[i];
            var (bg, card, text) = Previews[id];
            var selected = i == vm.ThemeIndex;
            var index = i;
            var content = new Grid { Padding = new Thickness(18, 16, 18, 16), RowSpacing = 10 };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition());
            var name = new TextBlock { Text = vm.ThemeOptions[i], FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(text) };
            var head = new Grid(); head.Children.Add(name);
            if (selected) head.Children.Add(new FontIcon { Glyph = "", FontSize = 14, Foreground = Brush(text), HorizontalAlignment = HorizontalAlignment.Right });
            content.Children.Add(head);
            var bar = new Border { Height = 7, Width = 120, CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Left, Background = Brush(id == "Light" ? fill : Lighter(fill)) };
            Grid.SetRow(bar, 1); content.Children.Add(bar);
            var hint = new TextBlock { Text = t["ApHint" + id], FontSize = 11.5, Foreground = Brush(text), Opacity = 0.75, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(hint, 2); content.Children.Add(hint);
            var surface = new Border
            {
                Child = content, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 12, 12),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = selected ? (Brush)Application.Current.Resources["RoseBrush"] : (Brush)Application.Current.Resources["CardBorderBrush"],
            };
            surface.Background = id == "System"
                ? new LinearGradientBrush(new GradientStopCollection { new() { Color = Hex("#FFFFFF"), Offset = 0.5 }, new() { Color = Hex("#211A29"), Offset = 0.5 } }, 0) { StartPoint = new(0, 0), EndPoint = new(1, 1) }
                : Brush(card);
            if (id == "System") { name.Foreground = Brush("#281C30"); hint.Foreground = Brush("#F8F5FA"); hint.Opacity = 0.9; foreach (var icon in head.Children.OfType<FontIcon>()) icon.Foreground = Brush("#281C30"); }
            var button = new Button { Content = surface, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            AutomationProperties.SetName(button, vm.ThemeOptions[i] + (selected ? " ✓" : ""));
            AutomationProperties.SetAutomationId(button, "theme-" + id.ToLowerInvariant());
            button.Click += (_, _) => { vm.ThemeIndex = index; UiSound.Play(UiSound.Cue.Toggle); };
            ThemeCards.Items.Add(button);
        }
        AccentDots.Children.Clear();
        foreach (var (id, label, hex) in AccentList)
        {
            var selected = id == vm.Accent;
            var dot = new Grid { Width = 44, Height = 44 };
            dot.Children.Add(new Ellipse { Fill = Brush(hex) });
            if (selected)
            {
                dot.Children.Add(new Ellipse { Stroke = (Brush)Application.Current.Resources["MainTextBrush"], StrokeThickness = 2, Margin = new Thickness(-4) });
                dot.Children.Add(new FontIcon { Glyph = "", FontSize = 16, Foreground = new SolidColorBrush(Colors.White) });
            }
            var b = new Button { Content = dot, Width = 52, Height = 52, Padding = new Thickness(0), CornerRadius = new CornerRadius(26), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
            ToolTipService.SetToolTip(b, t[label]);
            AutomationProperties.SetName(b, t[label] + (selected ? " ✓" : ""));
            AutomationProperties.SetAutomationId(b, "accent-" + id);
            var accentId = id;
            b.Click += (_, _) => { vm.Accent = accentId; Motion.Pop(b); UiSound.Play(UiSound.Cue.Toggle); };
            AccentDots.Children.Add(b);
            if (selected) AccentName.Text = t[label];
        }
    }

    private static Color Hex(string hex) => Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    private static SolidColorBrush Brush(string hex) => new(Hex(hex));
    private static string Lighter(string hex)
    {
        var c = Hex(hex);
        byte L(byte v) => (byte)(v + (255 - v) * 0.45);
        return $"#{L(c.R):X2}{L(c.G):X2}{L(c.B):X2}";
    }
    private static class Colors { public static Color Transparent => Color.FromArgb(0, 0, 0, 0); public static Color White => Color.FromArgb(255, 255, 255, 255); }
}
