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

    // Visual 3.0: a GridView (ItemsWrapGrid) that fills its row the same way: Fit.GridMinItemWidth on the GridView.
    public static readonly DependencyProperty GridMinItemWidthProperty = DependencyProperty.RegisterAttached("GridMinItemWidth", typeof(double), typeof(Fit),
        new PropertyMetadata(0.0, (d, e) => { if (d is GridView v && e.NewValue is double w && w > 0) AttachGrid(v, w); }));
    public static double GetGridMinItemWidth(DependencyObject d) => (double)d.GetValue(GridMinItemWidthProperty);
    public static void SetGridMinItemWidth(DependencyObject d, double value) => d.SetValue(GridMinItemWidthProperty, value);
    private static void AttachGrid(GridView view, double min)
    {
        void Apply(double width)
        {
            if (view.ItemsPanelRoot is not ItemsWrapGrid panel || width < min) return;
            var columns = Math.Max(1, (int)(width / min));
            var item = Math.Floor(width / columns);
            if (Math.Abs(panel.ItemWidth - item) > 0.5) panel.ItemWidth = item;
        }
        view.SizeChanged += (_, e) => Apply(e.NewSize.Width);
        view.Loaded += (_, _) => Apply(view.ActualWidth);
    }

    // Visual 3.0: keeps an element's height a fixed multiple of its width (covers stay portrait as columns widen).
    public static readonly DependencyProperty AspectProperty = DependencyProperty.RegisterAttached("Aspect", typeof(double), typeof(Fit),
        new PropertyMetadata(0.0, (d, e) => { if (d is FrameworkElement f && e.NewValue is double r && r > 0) f.SizeChanged += (_, a) => { var h = Math.Round(a.NewSize.Width * GetAspect(f)); if (a.NewSize.Width > 0 && Math.Abs(f.Height - h) > 0.5) f.Height = h; }; }));
    public static double GetAspect(DependencyObject d) => (double)d.GetValue(AspectProperty);
    public static void SetAspect(DependencyObject d, double value) => d.SetValue(AspectProperty, value);

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
