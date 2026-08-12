using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PIDTuner.Desktop.Controls;

public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.All(value => value is true) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }
}
