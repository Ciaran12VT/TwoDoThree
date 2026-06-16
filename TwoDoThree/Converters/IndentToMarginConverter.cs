using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TwoDoThree.Converters;

public sealed class IndentToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var indent = value is int level ? Math.Max(0, level) : 0;
        return new Thickness(indent * 28, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
