using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using TwoDoThree.Models;

namespace TwoDoThree.Views;

public partial class TaskStatusPeriodEditWindow : Window, INotifyPropertyChanged
{
    private string startText;
    private string endText;
    private string statusMessage;
    private string errorMessage = string.Empty;

    public TaskStatusPeriodEditWindow(TaskStatusPeriod period)
    {
        Period = period;
        StatusDisplay = period.StatusDisplay;
        startText = period.StartTime.ToString("g", CultureInfo.CurrentCulture);
        endText = period.EndTime.ToString("g", CultureInfo.CurrentCulture);
        statusMessage = period.StatusMessage;
        CanEditEndTime = period.CanEditEndTime;
        CanEditStatusMessage = period.CanEditStatusMessage;
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TaskStatusPeriod Period { get; }

    public string StatusDisplay { get; }

    public bool CanEditEndTime { get; }

    public bool CanEditStatusMessage { get; }

    public DateTime StartTime { get; private set; }

    public DateTime EndTime { get; private set; }

    public string EditedStatusMessage { get; private set; } = string.Empty;

    public string StartText
    {
        get => startText;
        set => SetProperty(ref startText, value);
    }

    public string EndText
    {
        get => endText;
        set => SetProperty(ref endText, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        set => SetProperty(ref errorMessage, value);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DateTime.TryParse(StartText, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsedStart))
        {
            ErrorMessage = "Enter a valid start date and time.";
            return;
        }

        var parsedEnd = Period.EndTime;
        if (CanEditEndTime
            && !DateTime.TryParse(EndText, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsedEnd))
        {
            ErrorMessage = "Enter a valid end date and time.";
            return;
        }

        StartTime = parsedStart;
        EndTime = parsedEnd;
        EditedStatusMessage = StatusMessage.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SetProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
