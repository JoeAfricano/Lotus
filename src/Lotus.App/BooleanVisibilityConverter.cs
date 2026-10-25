using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
namespace Lotus.App;
public sealed class BooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (parameter as string == "Invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Dims secondary items (for example disabled startup entries) without hiding them.
public sealed class BooleanOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? 0.55 : 1.0;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
// Picks one of two resources (brushes, thickness) from a boolean, e.g. to highlight the selected profile card.
public sealed class BooleanChoiceConverter : IValueConverter
{
    public object? TrueValue { get; set; }
    public object? FalseValue { get; set; }
    public object? Convert(object value, Type targetType, object parameter, string language) => value is true ? TrueValue : FalseValue;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
