using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Lotus.App;

// Texts may mark emphasis with **double asterisks**; Rich.Text renders those parts in bold.
// Usage in XAML: <TextBlock local:Rich.Text="{Binding Text[Key]}"/>; in code: RichText.Apply(textBlock, value).
public static class Rich
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Rich), new PropertyMetadata(null, (d, e) => { if (d is TextBlock t) RichText.Apply(t, e.NewValue as string); }));
    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);
}

public static class RichText
{
    public static void Apply(TextBlock block, string? value)
    {
        block.Inlines.Clear();
        if (string.IsNullOrEmpty(value)) return;
        var bold = false;
        foreach (var part in value.Split("**"))
        {
            if (part.Length > 0) block.Inlines.Add(new Run { Text = part, FontWeight = bold ? FontWeights.SemiBold : block.FontWeight });
            bold = !bold;
        }
    }
    public static TextBlock Block(string value, double? size = null, double opacity = 1)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = opacity };
        if (size is { } s) t.FontSize = s;
        Apply(t, value);
        return t;
    }
    public static string Plain(string value) => value.Replace("**", "");
}
