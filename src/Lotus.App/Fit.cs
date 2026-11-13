using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App;

// Visual 2.0: a wrap grid with Fit.MinItemWidth fills its whole row. It shows as many columns of at least that
// width as fit, then widens them to the edge, so there is no empty strip on the right of galleries.
public static class Fit
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.RegisterAttached("MinItemWidth", typeof(double), typeof(Fit),
        new PropertyMetadata(0.0, (d, e) => { if (d is VariableSizedWrapGrid g && e.NewValue is double w && w > 0) Attach(g, w); }));
    public static double GetMinItemWidth(DependencyObject d) => (double)d.GetValue(MinItemWidthProperty);
    public static void SetMinItemWidth(DependencyObject d, double value) => d.SetValue(MinItemWidthProperty, value);

    private static void Attach(VariableSizedWrapGrid grid, double min)
    {
        void Apply(double width)
        {
            if (width < min) return;
            var columns = Math.Max(1, (int)(width / min));
            var item = Math.Floor(width / columns);
            if (Math.Abs(grid.ItemWidth - item) > 0.5) grid.ItemWidth = item;
        }
        grid.SizeChanged += (_, e) => Apply(e.NewSize.Width);
        grid.Loaded += (_, _) => Apply(grid.ActualWidth);
    }
}
