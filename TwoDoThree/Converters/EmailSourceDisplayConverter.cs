using System.Globalization;
using System.Windows.Data;
using TwoDoThree.Models;

namespace TwoDoThree.Converters;

public sealed class EmailSourceDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            EmailSource.MicrosoftGraph => "Microsoft Graph",
            EmailSource.ClassicOutlook => "Classic Outlook",
            EmailSource.ManualImport => "Manual Import",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
