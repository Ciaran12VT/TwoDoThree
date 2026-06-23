using System.ComponentModel;
using System.Windows;
using TwoDoThree.Models;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Views;

public partial class StatusMessageWindow : Window, INotifyPropertyChanged
{
    private string message = string.Empty;

    public StatusMessageWindow(TaskItemStatus status, string initialMessage = "")
    {
        Status = status;
        Prompt = $"Why is this task being set to {FormatStatus(status)}?";
        message = initialMessage.Trim();
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            StatusMessageTextBox.Focus();
            StatusMessageTextBox.SelectAll();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TaskItemStatus Status { get; }

    public string Prompt { get; }

    public string Message
    {
        get => message;
        set
        {
            if (message == value)
            {
                return;
            }

            message = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
        }
    }

    public static bool RequiresMessage(TaskItemStatus status)
    {
        return status is TaskItemStatus.Blocked
            or TaskItemStatus.OnHold
            or TaskItemStatus.Complete
            or TaskItemStatus.Cancelled;
    }

    public static bool TryPrompt(Window owner, TaskItemStatus status, out string statusMessage)
    {
        return TryPrompt(owner, status, string.Empty, out statusMessage);
    }

    public static bool TryPrompt(Window owner, TaskItem task, TaskItemStatus status, out string statusMessage)
    {
        return TryPrompt(owner, status, task.GetPreviousStatusMessage(status), out statusMessage);
    }

    public static bool TryPrompt(Window owner, TaskItemStatus status, string initialMessage, out string statusMessage)
    {
        statusMessage = string.Empty;
        if (!RequiresMessage(status))
        {
            return true;
        }

        var window = new StatusMessageWindow(status, initialMessage)
        {
            Owner = owner
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        statusMessage = window.Message.Trim();
        return true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Message = Message.Trim();
        if (string.IsNullOrWhiteSpace(Message))
        {
            StatusMessageTextBox.Focus();
            StatusMessageTextBox.SelectAll();
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string FormatStatus(TaskItemStatus status)
    {
        return status switch
        {
            TaskItemStatus.InProgress => "In-Progress",
            TaskItemStatus.OnHold => "On Hold",
            _ => status.ToString()
        };
    }
}
