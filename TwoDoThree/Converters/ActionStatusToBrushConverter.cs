using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TwoDoThree.Models;

namespace TwoDoThree.Converters;

public sealed class ActionStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            ActionStatus.InProgress => new SolidColorBrush(Color.FromRgb(191, 219, 254)),
            ActionStatus.Completed => new SolidColorBrush(Color.FromRgb(187, 247, 208)),
            ActionStatus.Failed => new SolidColorBrush(Color.FromRgb(254, 202, 202)),
            ActionStatus.Cancelled => new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            _ => Brushes.White
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
