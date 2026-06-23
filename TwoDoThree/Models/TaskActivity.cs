using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class TaskActivity : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private DateTime occurredOn = DateTime.Now;
    private string activity = string.Empty;
    private TaskStatus? fromStatus;
    private TaskStatus? toStatus;
    private string statusMessage = string.Empty;

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public DateTime OccurredOn
    {
        get => occurredOn;
        set => SetProperty(ref occurredOn, value);
    }

    public string Activity
    {
        get => activity;
        set => SetProperty(ref activity, value ?? string.Empty);
    }

    public TaskStatus? FromStatus
    {
        get => fromStatus;
        set => SetProperty(ref fromStatus, value);
    }

    public TaskStatus? ToStatus
    {
        get => toStatus;
        set => SetProperty(ref toStatus, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value ?? string.Empty);
    }
}
