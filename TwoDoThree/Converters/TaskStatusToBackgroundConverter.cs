using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Converters;

public sealed class TaskStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            TaskItemStatus.Active => new SolidColorBrush(Color.FromRgb(187, 247, 208)),
            TaskItemStatus.InProgress => new SolidColorBrush(Color.FromRgb(191, 219, 254)),
            TaskItemStatus.OnHold => new SolidColorBrush(Color.FromRgb(254, 215, 170)),
            TaskItemStatus.Blocked => new SolidColorBrush(Color.FromRgb(254, 202, 202)),
            TaskItemStatus.Complete => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            TaskItemStatus.Cancelled => new SolidColorBrush(Color.FromRgb(209, 213, 219)),
            _ => new SolidColorBrush(Color.FromRgb(229, 231, 235))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
