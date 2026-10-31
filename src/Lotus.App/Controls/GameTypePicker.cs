using Lotus.Core;
using Lotus.Core.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App.Controls;

// "What kind of game are you looking for?": the kinds players pick most, a search, and every genre and theme grouped
// under "Advanced". Several can be chosen; the same kind shown in two places stays in sync.
public sealed class GameTypePicker : UserControl
{
    private readonly ITextCatalog text;
    private readonly HashSet<string> selected;
    private readonly Dictionary<string, List<ToggleButton>> chips = [];
    private readonly List<(FrameworkElement Section, List<ToggleButton> Chips)> sections = [];
    private readonly Expander advanced;
    private readonly TextBlock count, noMatch;
    private readonly RadioButtons match;
    private bool syncing;

    public IReadOnlyCollection<string> Selected => selected;
    public bool MatchAll => match.SelectedIndex == 1;

    public GameTypePicker(ITextCatalog text, IEnumerable<string> initial, bool matchAll)
    {
        this.text = text;
        selected = [.. initial];
        var root = new StackPanel { Spacing = 16, Width = 880 };
        root.Children.Add(new TextBlock { Text = text["GtIntro"], TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });

        var search = new TextBox { PlaceholderText = text["GtSearch"], MinHeight = 40 };
        AutomationProperties(search, "gametype-search", text["GtSearch"]);
        search.TextChanged += (_, _) => Filter(search.Text);
        root.Children.Add(search);

        root.Children.Add(Section(text["GtPopular"], "", GameTypes.Popular, out var popular));
        sections.Add(popular);

        var groups = new StackPanel { Spacing = 18, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var g in GameTypes.Groups)
        {
            groups.Children.Add(Section(text["GtGroup" + g.Key], g.Glyph, g.Options, out var section));
            sections.Add(section);
        }
        advanced = new Expander
        {
            Header = new TextBlock { Text = text["GtAdvanced"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            Content = groups, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = selected.Any(id => !GameTypes.Popular.Any(p => p.Id == id)), // open when something from it is chosen
        };
        AutomationProperties(advanced, "gametype-advanced", text["GtAdvanced"]);
        root.Children.Add(advanced);
        noMatch = new TextBlock { Text = text["GtNoMatch"], Opacity = 0.7, Visibility = Visibility.Collapsed };
        root.Children.Add(noMatch);

        match = new RadioButtons { Header = text["GtMatch"], MaxColumns = 2, SelectedIndex = matchAll ? 1 : 0 };
        match.Items.Add(text["GtMatchAny"]); match.Items.Add(text["GtMatchAll"]);
        AutomationProperties(match, "gametype-match", text["GtMatch"]);
        root.Children.Add(match);

        count = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["RoseBrush"] };
        root.Children.Add(count);
        UpdateCount();
        Content = new ScrollViewer { Content = root, MaxHeight = 560, Padding = new Thickness(0, 0, 14, 0) };
    }

    private static void AutomationProperties(UIElement e, string id, string name)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(e, id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(e, name);
    }

    private StackPanel Section(string title, string glyph, IEnumerable<GameTypeOption> options, out (FrameworkElement, List<ToggleButton>) section)
    {
        var panel = new StackPanel { Spacing = 10 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(9), Background = (Brush)Application.Current.Resources["HeroBrush"],
            Child = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = (Brush)Application.Current.Resources["GoldBrush"] },
        });
        header.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(header);
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 216, ItemHeight = 44 };
        var list = new List<ToggleButton>();
        foreach (var o in options)
        {
            var label = text[o.LabelKey];
            var chip = new ToggleButton
            {
                Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis },
                Tag = o.Id, IsChecked = selected.Contains(o.Id), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 6, 14, 6), HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            ToolTipService.SetToolTip(chip, label);
            AutomationProperties(chip, "gametype-" + o.Id, label);
            chip.Checked += (_, _) => Toggle(o.Id, true);
            chip.Unchecked += (_, _) => Toggle(o.Id, false);
            if (!chips.TryGetValue(o.Id, out var same)) chips[o.Id] = same = [];
            same.Add(chip);
            list.Add(chip);
            grid.Children.Add(chip);
        }
        panel.Children.Add(grid);
        section = (panel, list);
        return panel;
    }

    private void Toggle(string id, bool on)
    {
        if (syncing) return;
        if (on) selected.Add(id); else selected.Remove(id);
        syncing = true;
        foreach (var chip in chips[id]) chip.IsChecked = on;
        syncing = false;
        UpdateCount();
    }

    public void Clear()
    {
        syncing = true;
        foreach (var list in chips.Values) foreach (var chip in list) chip.IsChecked = false;
        syncing = false;
        selected.Clear(); UpdateCount();
    }

    private void UpdateCount() => count.Text = selected.Count == 0 ? text["GtNone"] : text.Format("GtSelected", selected.Count);

    // Typing narrows every section to matching kinds (and opens "Advanced" so nothing is hidden).
    private void Filter(string query)
    {
        var q = query.Trim();
        var any = false;
        foreach (var (section, list) in sections)
        {
            var visible = 0;
            foreach (var chip in list)
            {
                var show = q.Length == 0 || ((chip.Content as TextBlock)?.Text ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase)
                    || ((string)chip.Tag).Contains(q, StringComparison.OrdinalIgnoreCase);
                chip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) visible++;
            }
            section.Visibility = visible > 0 ? Visibility.Visible : Visibility.Collapsed;
            any |= visible > 0;
        }
        if (q.Length > 0) advanced.IsExpanded = true;
        noMatch.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}
