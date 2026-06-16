using System.Globalization;
using System.Windows.Data;
using TwoDoThree.Models;

namespace TwoDoThree.Converters;

public sealed class StatusDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            ActionStatus.NotStarted => "Not started",
            ActionStatus.InProgress => "In progress",
            ActionStatus.Completed => "Completed",
            ActionStatus.Failed => "Failed",
            ActionStatus.Cancelled => "Cancelled",
            TwoDoThree.Models.TaskStatus.Inactive => "Inactive",
            TwoDoThree.Models.TaskStatus.Active => "Active",
            TwoDoThree.Models.TaskStatus.InProgress => "In-Progress",
            TwoDoThree.Models.TaskStatus.OnHold => "On Hold",
            TwoDoThree.Models.TaskStatus.Blocked => "Blocked",
            TwoDoThree.Models.TaskStatus.Complete => "Complete",
            TwoDoThree.Models.TaskStatus.Cancelled => "Cancelled",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
