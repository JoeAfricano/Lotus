using System.Runtime.InteropServices.WindowsRuntime;
using Lotus.Core.Display;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Foundation;

namespace Lotus.App.Controls;

// Local image-only draft: latest request wins, one small bitmap per side, no DDC or driver calls.
public sealed class DisplayComparisonPreview : UserControl
{
    private readonly Grid host = new();
    private readonly Image original = new() { Stretch = Stretch.Uniform }, adjusted = new() { Stretch = Stretch.Uniform };
    private readonly RectangleGeometry clip = new();
    private readonly Grid divider = new() { Width = 32, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Slider split = new() { Minimum = 0, Maximum = 1, Value = .5, StepFrequency = .01 };
    private readonly Grid labels = new();
    private byte[]? pixels;
    private byte[]? workBuffer;
    private WriteableBitmap? rendered;
    private CalibrationProfile look = new();
    private CalibrationProfile? applied;
    private CalibrationState? baseline;
    private string? asset;
    private int generation, revision, width, height;
    private bool dirty, processing, dragging, physical;
    public DisplayComparisonPreview()
    {
        var root = new StackPanel { Spacing = 6 }; Content = root;
        adjusted.Clip = clip; host.Children.Add(original); host.Children.Add(adjusted);
        divider.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Microsoft.UI.Colors.White) });
        divider.Children.Add(new Border { Width = 32, Height = 44, CornerRadius = new(12), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(245, 32, 16, 25)), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = new TextBlock { Text = "↔", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 21,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        host.Children.Add(divider); host.Children.Add(labels);
        root.Children.Add(new Border { CornerRadius = new(16), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20)), Child = host });
        root.Children.Add(split);
        split.ValueChanged += (_, _) => Position(); host.SizeChanged += (_, _) => Position();
        divider.PointerPressed += (_, e) => { if (physical) return; dragging = divider.CapturePointer(e.Pointer); Move(e.GetCurrentPoint(host).Position.X); e.Handled = true; };
        divider.PointerMoved += (_, e) => { if (dragging) { Move(e.GetCurrentPoint(host).Position.X); e.Handled = true; } };
        divider.PointerReleased += (_, e) => { dragging = false; divider.ReleasePointerCapture(e.Pointer); e.Handled = true; };
        divider.PointerCanceled += (_, _) => dragging = false;
        Unloaded += (_, _) => { ++generation; ++revision; dirty = false; pixels = workBuffer = null; rendered = null; original.Source = adjusted.Source = null; asset = null; };
    }
    public void SetLabels(string before, string after, string accessible)
    {
        labels.Children.Clear();
        foreach (var (text, alignment) in new[] { (before, HorizontalAlignment.Left), (after, HorizontalAlignment.Right) })
            labels.Children.Add(new Border { HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Top, Margin = new(10), Padding = new(9, 4, 9, 4), CornerRadius = new(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 20, 12, 18)), Child = new TextBlock { Text = text, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 12 } });
        labels.IsHitTestVisible = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(split, accessible);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(divider, accessible);
    }
    private void Move(double x) { if (host.ActualWidth > 0) split.Value = Math.Clamp(x / host.ActualWidth, 0, 1); }
    private void Position()
    {
        if (width > 0) host.Height = Math.Min(420, Math.Max(1, host.ActualWidth) * height / width);
        var x = host.ActualWidth * split.Value;
        clip.Rect = new Rect(x, 0, Math.Max(0, host.ActualWidth - x), Math.Max(0, host.ActualHeight));
        divider.Margin = new Thickness(x - 16, 0, 0, 0);
    }
    public void SetPhysicalPreview(bool value)
    {
        if (physical == value) return;
        physical = value; ++revision;
        divider.Visibility = labels.Visibility = split.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        adjusted.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        if (!value) Request(look, baseline, applied);
    }
    public async Task SetSceneAsync(string name)
    {
        if (asset == name) return;
        asset = name; var ticket = ++generation; ++revision;
        try
        {
            var decoded = await Task.Run(async () =>
            {
                using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "ColorCalibration", name));
                using var stream = file.AsRandomAccessStream(); var decoder = await BitmapDecoder.CreateAsync(stream);
                var w = Math.Min(800u, decoder.PixelWidth); var h = Math.Max(1u, (uint)Math.Round((double)w * decoder.PixelHeight / decoder.PixelWidth));
                var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform { ScaledWidth = w, ScaledHeight = h },
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
                return (Bytes: data.DetachPixelData(), Width: (int)w, Height: (int)h);
            });
            if (ticket != generation || !IsLoaded) return;
            pixels = decoded.Bytes; workBuffer = new byte[pixels.Length]; width = decoded.Width; height = decoded.Height;
            var source = new WriteableBitmap(width, height); using (var output = source.PixelBuffer.AsStream()) output.Write(pixels); source.Invalidate();
            original.Source = source; rendered = new(width, height);
            using (var output = rendered.PixelBuffer.AsStream()) output.Write(pixels); rendered.Invalidate();
            adjusted.Source = rendered; Position(); Request(look, baseline, applied);
        }
        catch (Exception ex) { if (ticket == generation) asset = null; Serilog.Log.Warning(ex, "Local display scene preview"); }
    }
    public void Request(CalibrationProfile value, CalibrationState? state, CalibrationProfile? current = null)
    {
        look = value; baseline = state; applied = current; ++revision;
        if (physical || pixels is null || state is null) return;
        dirty = true;
        if (!processing) _ = RenderAsync();
    }
    private async Task RenderAsync()
    {
        processing = true;
        try
        {
            while (dirty && !physical && pixels is { } source && workBuffer is { } buffer && baseline is { } state && IsLoaded)
            {
                dirty = false; var ticket = revision; var value = look; var existing = applied;
                var output = await Task.Run(() => { DisplayImagePreview.RenderInto(source, buffer, value, state, existing); return buffer; });
                if (ticket != revision || physical || !IsLoaded || rendered is null) continue;
                using (var stream = rendered.PixelBuffer.AsStream()) stream.Write(output);
                rendered.Invalidate();
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Local display preview transform"); }
        finally { processing = false; }
    }
}
