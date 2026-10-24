using Lotus.Core.Benchmark;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Lotus.App.Controls;

public sealed class FrametimeChart : Grid
{
    private readonly Canvas plot = new();
    private readonly TextBlock scale = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock time = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Bottom };
    private IReadOnlyList<FrameSample> frames = [];
    public FrametimeChart()
    {
        IsHitTestVisible = false;
        plot.Margin = new Thickness(64, 4, 4, 24); Children.Add(plot); Children.Add(scale); Children.Add(time);
        plot.SizeChanged += (_, _) => Draw();
    }
    public void SetData(IReadOnlyList<FrameSample>? data)
    {
        var source = data ?? [];
        frames = source.Count <= 600 ? source : source.Chunk((int)Math.Ceiling(source.Count / 600d)).Select(b => b.MaxBy(p => p.Milliseconds)!).ToArray();
        Draw();
    }
    private void Draw()
    {
        plot.Children.Clear(); scale.Text = "— ms"; time.Text = "";
        if (frames.Count < 2 || plot.ActualWidth <= 0 || plot.ActualHeight <= 0) return;
        var max = frames.Max(f => f.Milliseconds); var start = frames[0].TimeSeconds; var span = frames[^1].TimeSeconds - start;
        if (max <= 0 || span <= 0) return;
        scale.Text = $"{max:F1} ms\n\n0 ms"; time.Text = $"{start:F1} s → {frames[^1].TimeSeconds:F1} s";
        var geometry = new PathGeometry(); var figure = new PathFigure { IsFilled = false }; geometry.Figures.Add(figure);
        for (var i = 0; i < frames.Count; i++)
        {
            var point = new Point((frames[i].TimeSeconds - start) / span * plot.ActualWidth, (1 - frames[i].Milliseconds / max) * plot.ActualHeight);
            if (i == 0) figure.StartPoint = point; else figure.Segments.Add(new LineSegment { Point = point });
        }
        plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = (Brush)Application.Current.Resources["RoseBrush"], StrokeThickness = 1.5 });
    }
}
