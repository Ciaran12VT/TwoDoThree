using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TwoDoThree.Models;

namespace TwoDoThree.Views;

public partial class OutOfHoursConfirmationWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer countdownTimer;
    private int secondsRemaining = 10;
    private bool dontAskAgainToday;

    public OutOfHoursConfirmationWindow(TaskItem task)
    {
        TaskTitle = string.IsNullOrWhiteSpace(task.Title)
            ? $"Task {task.Id}"
            : task.Title;
        InitializeComponent();
        DataContext = this;
        countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        countdownTimer.Tick += CountdownTimer_Tick;
        Loaded += (_, _) => countdownTimer.Start();
        Closed += (_, _) => countdownTimer.Stop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TaskTitle { get; }

    public string CountdownText => $"Activity will be stopped in {secondsRemaining} second{(secondsRemaining == 1 ? string.Empty : "s")}.";

    public bool DontAskAgainToday
    {
        get => dontAskAgainToday;
        set
        {
            if (dontAskAgainToday == value)
            {
                return;
            }

            dontAskAgainToday = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DontAskAgainToday)));
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        secondsRemaining--;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownText)));
        if (secondsRemaining <= 0)
        {
            DialogResult = false;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
