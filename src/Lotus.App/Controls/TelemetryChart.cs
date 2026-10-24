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
    private readonly TextBlock nowLabel = new();
    private IReadOnlyList<SensorHistoryPoint> points = Array.Empty<SensorHistoryPoint>();
    public TelemetryChart()
    {
        IsHitTestVisible = false;
        Background = (Brush)Application.Current.Resources["ShellBrush"];
        trace.Stroke = (Brush)Application.Current.Resources["RoseBrush"];
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scale = new Grid { Margin = new Thickness(0, 0, 5, 0) };
        foreach (var (label, alignment) in new[] { ("100%", VerticalAlignment.Top), ("50%", VerticalAlignment.Center), ("0%", VerticalAlignment.Bottom) })
            scale.Children.Add(new TextBlock { Text = label, FontSize = 10, VerticalAlignment = alignment,
                HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        Children.Add(scale);
        Grid.SetColumn(canvas, 1);
        Children.Add(canvas);
        var timeAxis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        for (var i = 0; i < 3; i++) timeAxis.ColumnDefinitions.Add(new ColumnDefinition());
        var labels = new[] { new TextBlock { Text = "−60 s", HorizontalAlignment = HorizontalAlignment.Left },
            new TextBlock { Text = "−30 s", HorizontalAlignment = HorizontalAlignment.Center }, nowLabel };
        nowLabel.HorizontalAlignment = HorizontalAlignment.Right;
        for (var i = 0; i < labels.Length; i++)
        {
            labels[i].FontSize = 10; labels[i].Foreground = (Brush)Application.Current.Resources["MutedBrush"];
            Grid.SetColumn(labels[i], i); timeAxis.Children.Add(labels[i]);
        }
        Grid.SetColumn(timeAxis, 1); Grid.SetRow(timeAxis, 1); Children.Add(timeAxis);
        Loaded += (_, _) => nowLabel.Text = App.Text["AxisNow"];
        for (var i = 0; i <= 2; i++)
            canvas.Children.Add(new Line { Stroke = (Brush)Application.Current.Resources["MutedBrush"], Opacity = 0.18, StrokeThickness = 1 });
        canvas.Children.Add(trace);
        canvas.SizeChanged += (_, _) => Redraw();
    }
    public void SetPoints(IReadOnlyList<HistoryPoint> points, bool cpu) => SetPoints(points.Select(p => new SensorHistoryPoint(p.Time, cpu ? p.CpuPercent : p.MemoryPercent)).ToArray());
    public void SetPoints(IReadOnlyList<SensorHistoryPoint> points) { this.points = points; Redraw(); }
    private void Redraw()
    {
        var width = canvas.ActualWidth; var height = canvas.ActualHeight;
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
            var value = sample.Value;
            if (value is null) { figure = null; continue; }
            var point = new Point(Math.Max(1, width - 1 - (newest - sample.Time).TotalSeconds / 60 * (width - 2)),
                1 + (100 - value.Value) / 100 * (height - 2));
            if (figure is null) { figure = new PathFigure { StartPoint = point, IsFilled = false }; geometry.Figures.Add(figure); }
            else figure.Segments.Add(new LineSegment { Point = point });
        }
        trace.Data = geometry;
    }
}
