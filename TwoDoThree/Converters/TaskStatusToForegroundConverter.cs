using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Converters;

public sealed class TaskStatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            TaskItemStatus.Complete or TaskItemStatus.Cancelled => new SolidColorBrush(Color.FromRgb(55, 65, 81)),
            _ => new SolidColorBrush(Color.FromRgb(17, 24, 39))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
