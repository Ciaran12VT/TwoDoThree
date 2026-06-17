using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TwoDoThree.Models;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Views;

public partial class TaskFilterWindow : Window, INotifyPropertyChanged
{
    private string minTimeSpentHoursText = string.Empty;
    private string maxTimeSpentHoursText = string.Empty;

    public TaskFilterWindow(TaskFilterSet? currentFilterSet)
    {
        FilterSet = currentFilterSet?.Clone() ?? new TaskFilterSet
        {
            Name = "New Filter"
        };
        MinTimeSpentHoursText = FilterSet.MinTimeSpentHours?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        MaxTimeSpentHoursText = FilterSet.MaxTimeSpentHours?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;

        InitializeComponent();
        DataContext = this;
        SetStatusCheckBoxes();
        Loaded += (_, _) =>
        {
            FilterNameTextBox.Focus();
            FilterNameTextBox.SelectAll();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TaskFilterSet FilterSet { get; }

    public string MinTimeSpentHoursText
    {
        get => minTimeSpentHoursText;
        set
        {
            if (minTimeSpentHoursText == value)
            {
                return;
            }

            minTimeSpentHoursText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinTimeSpentHoursText)));
        }
    }

    public string MaxTimeSpentHoursText
    {
        get => maxTimeSpentHoursText;
        set
        {
            if (maxTimeSpentHoursText == value)
            {
                return;
            }

            maxTimeSpentHoursText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxTimeSpentHoursText)));
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        FilterSet.Name = FilterSet.Name.Trim();
        if (string.IsNullOrWhiteSpace(FilterSet.Name))
        {
            FilterNameTextBox.Focus();
            FilterNameTextBox.SelectAll();
            return;
        }

        if (!TryReadHours(MinTimeSpentHoursText, out var minHours))
        {
            FocusInvalidHours(MinTimeSpentTextBox);
            return;
        }

        if (!TryReadHours(MaxTimeSpentHoursText, out var maxHours))
        {
            FocusInvalidHours(MaxTimeSpentTextBox);
            return;
        }

        if (minHours.HasValue && maxHours.HasValue && minHours.Value > maxHours.Value)
        {
            FocusInvalidHours(MinTimeSpentTextBox);
            return;
        }

        FilterSet.MinTimeSpentHours = minHours;
        FilterSet.MaxTimeSpentHours = maxHours;
        FilterSet.IncludedStatuses = GetIncludedStatuses();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SetStatusCheckBoxes()
    {
        InactiveStatusCheckBox.IsChecked = FilterSet.IncludedStatuses.Contains(TaskItemStatus.Inactive);
        ActiveStatusCheckBox.IsChecked = FilterSet.IncludedStatuses.Contains(TaskItemStatus.Active);
        InProgressStatusCheckBox.IsChecked = FilterSet.IncludedStatuses.Contains(TaskItemStatus.InProgress);
        OnHoldStatusCheckBox.IsChecked = FilterSet.IncludedStatuses.Contains(TaskItemStatus.OnHold);
        BlockedStatusCheckBox.IsChecked = FilterSet.IncludedStatuses.Contains(TaskItemStatus.Blocked);
        CompleteStatusCheckBox.IsChecked = FilterSet.IncludedStatuses.Contains(TaskItemStatus.Complete);
        CancelledStatusCheckBox.IsChecked = FilterSet.IncludedStatuses.Contains(TaskItemStatus.Cancelled);
    }

    private List<TaskItemStatus> GetIncludedStatuses()
    {
        var statuses = new List<TaskItemStatus>();
        AddStatusIfChecked(statuses, InactiveStatusCheckBox, TaskItemStatus.Inactive);
        AddStatusIfChecked(statuses, ActiveStatusCheckBox, TaskItemStatus.Active);
        AddStatusIfChecked(statuses, InProgressStatusCheckBox, TaskItemStatus.InProgress);
        AddStatusIfChecked(statuses, OnHoldStatusCheckBox, TaskItemStatus.OnHold);
        AddStatusIfChecked(statuses, BlockedStatusCheckBox, TaskItemStatus.Blocked);
        AddStatusIfChecked(statuses, CompleteStatusCheckBox, TaskItemStatus.Complete);
        AddStatusIfChecked(statuses, CancelledStatusCheckBox, TaskItemStatus.Cancelled);
        return statuses;
    }

    private static void AddStatusIfChecked(ICollection<TaskItemStatus> statuses, CheckBox checkBox, TaskItemStatus status)
    {
        if (checkBox.IsChecked == true)
        {
            statuses.Add(status);
        }
    }

    private static bool TryReadHours(string text, out double? hours)
    {
        hours = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            || parsed < 0)
        {
            return false;
        }

        hours = parsed;
        return true;
    }

    private static void FocusInvalidHours(TextBox textBox)
    {
        textBox.Focus();
        textBox.SelectAll();
    }
}
