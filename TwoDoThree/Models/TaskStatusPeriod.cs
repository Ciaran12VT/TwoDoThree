namespace TwoDoThree.Models;

public sealed class TaskStatusPeriod
{
    public TaskStatus Status { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public DateTime StartTime { get; init; }

    public DateTime EndTime { get; init; }

    public bool IsCurrent { get; init; }

    public TaskActivity? StartActivity { get; init; }

    public TaskActivity? EndActivity { get; init; }

    public bool CanEditStatusMessage => StartActivity is not null;

    public bool CanEditEndTime => EndActivity is not null;

    public TimeSpan Duration => EndTime - StartTime;

    public double Hours => Math.Round(Math.Max(0, Duration.TotalHours), 4);

    public string StatusDisplay => Status switch
    {
        TaskStatus.InProgress => "In-Progress",
        TaskStatus.OnHold => "On Hold",
        _ => Status.ToString()
    };

    public string EndTimeDisplay => IsCurrent ? "Now" : EndTime.ToString("g");
}
