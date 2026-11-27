using Lotus.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// Big view of a game's store screenshot: covers the window, shows the full-size picture (the store's 1920-wide
// file, never the small thumbnail) fitted to the window, with arrows for the other screenshots.
// Closes with Esc, the X, or a click outside the picture; the arrow keys change the picture.
public static class ScreenshotViewer
{
    public static void Show(XamlRoot root, IReadOnlyList<string> shots, int index, ITextCatalog text)
    {
        if (shots.Count == 0) return;
        index = Math.Clamp(index, 0, shots.Count - 1);
        var popup = new Popup { XamlRoot = root };
        var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(72, 56, 72, 56), Opacity = 0 };
        var ring = new ProgressRing { IsActive = true, Width = 40, Height = 40, Foreground = new SolidColorBrush(Colors.White) };
        var counter = new TextBlock { Foreground = new SolidColorBrush(Colors.White), Opacity = 0.8, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20) };
        var close = RoundButton("", text["GiShotClose"], "game-shot-close");
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.Margin = new Thickness(0, 12, 12, 0);
        var prev = RoundButton("", text["GiShotPrev"], "game-shot-prev");
        prev.HorizontalAlignment = HorizontalAlignment.Left; prev.Margin = new Thickness(16, 0, 0, 0);
        var next = RoundButton("", text["GiShotNext"], "game-shot-next");
        next.HorizontalAlignment = HorizontalAlignment.Right; next.Margin = new Thickness(0, 0, 16, 0);
        var layer = new Grid
        {
            Width = root.Size.Width, Height = root.Size.Height,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 8, 4, 7)),
            Children = { ring, image, counter, prev, next, close },
        };
        AutomationProperties.SetAutomationId(layer, "game-shot-viewer");
        popup.Child = layer;

        void ShowAt(int i)
        {
            index = (i + shots.Count) % shots.Count;
            image.Opacity = 0; ring.IsActive = true; ring.Visibility = Visibility.Visible;
            var bitmap = new BitmapImage();
            bitmap.ImageOpened += (_, _) => { image.Opacity = 1; ring.IsActive = false; ring.Visibility = Visibility.Collapsed; };
            bitmap.ImageFailed += (_, _) => { ring.IsActive = false; ring.Visibility = Visibility.Collapsed; };
            if (Uri.TryCreate(shots[index], UriKind.Absolute, out var uri)) bitmap.UriSource = uri;
            image.Source = bitmap;
            counter.Text = $"{index + 1} / {shots.Count}";
            var many = shots.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            prev.Visibility = next.Visibility = counter.Visibility = many;
        }
        void Resize(XamlRoot r, XamlRootChangedEventArgs _) { layer.Width = r.Size.Width; layer.Height = r.Size.Height; }
        void Close() { popup.IsOpen = false; root.Changed -= Resize; }

        close.Click += (_, _) => Close();
        prev.Click += (_, _) => ShowAt(index - 1);
        next.Click += (_, _) => ShowAt(index + 1);
        image.Tapped += (_, e) => e.Handled = true; // a click on the picture itself keeps it open
        layer.Tapped += (_, e) => { if (!e.Handled) Close(); };
        layer.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Left && shots.Count > 1) { ShowAt(index - 1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Right && shots.Count > 1) { ShowAt(index + 1); e.Handled = true; }
        };
        root.Changed += Resize;
        ShowAt(index);
        popup.IsOpen = true;
        close.Focus(FocusState.Programmatic);
    }

    private static Button RoundButton(string glyph, string name, string id)
    {
        var b = new Button
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB3, 0x12, 0x09, 0x0F)), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White), Content = new FontIcon { Glyph = glyph, FontSize = 14 },
        };
        b.Tapped += (_, e) => e.Handled = true; // the button's own Click runs; the background must not close the view
        AutomationProperties.SetName(b, name); AutomationProperties.SetAutomationId(b, id);
        ToolTipService.SetToolTip(b, name);
        return b;
    }
}
