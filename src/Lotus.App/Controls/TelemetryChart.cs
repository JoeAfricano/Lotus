using Lotus.Core.Telemetry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Lotus.App.Controls;

// At most 60 vertices, one redraw per delivered sample; null values break the line.
public sealed class TelemetryChart : Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Path trace = new() { StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Canvas canvas = new();
    private IReadOnlyList<HistoryPoint> points = Array.Empty<HistoryPoint>();
    private bool cpu;
    public TelemetryChart()
    {
        IsHitTestVisible = false;
        Background = (Brush)Application.Current.Resources["ShellBrush"];
        trace.Stroke = (Brush)Application.Current.Resources["RoseBrush"];
        Children.Add(canvas);
        for (var i = 0; i <= 2; i++)
            canvas.Children.Add(new Line { Stroke = (Brush)Application.Current.Resources["MutedBrush"], Opacity = 0.18, StrokeThickness = 1 });
        canvas.Children.Add(trace);
        SizeChanged += (_, _) => Redraw();
    }
    public void SetPoints(IReadOnlyList<HistoryPoint> points, bool cpu) { this.points = points; this.cpu = cpu; Redraw(); }
    private void Redraw()
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        for (var i = 0; i <= 2; i++)
        {
            var line = (Line)canvas.Children[i]; line.X1 = 0; line.X2 = width; line.Y1 = line.Y2 = Math.Clamp(i * height / 2, 0.5, height - 0.5);
        }
        var geometry = new PathGeometry();
        PathFigure? figure = null;
        var newest = points.Count > 0 ? points[^1].Time : TimeSpan.Zero;
        TimeSpan? previous = null;
        foreach (var sample in points)
        {
            if (previous is { } time && sample.Time - time > TimeSpan.FromSeconds(2.5)) figure = null;
            previous = sample.Time;
            var value = cpu ? sample.CpuPercent : sample.MemoryPercent;
            if (value is null) { figure = null; continue; }
            var point = new Point(Math.Max(1, width - 1 - (newest - sample.Time).TotalSeconds / 60 * (width - 2)),
                1 + (100 - value.Value) / 100 * (height - 2));
            if (figure is null) { figure = new PathFigure { StartPoint = point, IsFilled = false }; geometry.Figures.Add(figure); }
            else figure.Segments.Add(new LineSegment { Point = point });
        }
        trace.Data = geometry;
    }
}
