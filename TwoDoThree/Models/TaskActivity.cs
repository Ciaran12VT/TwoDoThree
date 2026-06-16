namespace TwoDoThree.Models;

public sealed class TaskActivity
{
    public DateTime OccurredOn { get; init; } = DateTime.Now;

    public string Activity { get; init; } = string.Empty;

    public TaskStatus? FromStatus { get; init; }

    public TaskStatus? ToStatus { get; init; }
}
