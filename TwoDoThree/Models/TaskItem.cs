using System.Collections.ObjectModel;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class TaskItem : ObservableObject
{
    private int id;
    private string title = string.Empty;
    private string tags = string.Empty;
    private string pocs = string.Empty;
    private TaskStatus status = TaskStatus.Inactive;
    private TaskStatus statusBeforeActive = TaskStatus.Inactive;
    private int sortOrder;
    private DateTime? dueBy;
    private DateTime createdOn = DateTime.Now;
    private DateTime updatedOn = DateTime.Now;
    private TimeSpan timeSpent;
    private string surfScopeId = string.Empty;
    private string surfScopeName = string.Empty;

    public int Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Title
    {
        get => title;
        set
        {
            if (SetProperty(ref title, value))
            {
                UpdatedOn = DateTime.Now;
            }
        }
    }

    public string Tags
    {
        get => tags;
        set
        {
            if (SetProperty(ref tags, value))
            {
                UpdatedOn = DateTime.Now;
            }
        }
    }

    public string Pocs
    {
        get => pocs;
        set
        {
            if (SetProperty(ref pocs, value))
            {
                UpdatedOn = DateTime.Now;
            }
        }
    }

    public TaskStatus Status
    {
        get => status;
        set => SetStatus(value);
    }

    public TaskStatus StatusBeforeActive => statusBeforeActive;

    public int SortOrder
    {
        get => sortOrder;
        set => SetProperty(ref sortOrder, Math.Max(0, value));
    }

    public DateTime? DueBy
    {
        get => dueBy;
        set
        {
            if (SetProperty(ref dueBy, value))
            {
                UpdatedOn = DateTime.Now;
            }
        }
    }

    public DateTime CreatedOn
    {
        get => createdOn;
        set => SetProperty(ref createdOn, value);
    }

    public DateTime UpdatedOn
    {
        get => updatedOn;
        set => SetProperty(ref updatedOn, value);
    }

    public TimeSpan TimeSpent
    {
        get => timeSpent;
        set => SetProperty(ref timeSpent, value);
    }

    public string SurfScopeId
    {
        get => surfScopeId;
        set
        {
            if (SetProperty(ref surfScopeId, value))
            {
                UpdatedOn = DateTime.Now;
            }
        }
    }

    public string SurfScopeName
    {
        get => surfScopeName;
        set
        {
            if (SetProperty(ref surfScopeName, value))
            {
                UpdatedOn = DateTime.Now;
            }
        }
    }

    public ObservableCollection<ResourceItem> Resources { get; } = new();

    public ObservableCollection<ActionItem> Actions { get; } = new();

    public ObservableCollection<TaskActivity> Activities { get; } = new();

    public string CurrentStatusMessage =>
        GetPreviousStatusMessage(Status);

    public bool HasCurrentStatusMessage => !string.IsNullOrWhiteSpace(CurrentStatusMessage);

    public string GetPreviousStatusMessage(TaskStatus targetStatus)
    {
        return Activities
            .Where(activity => activity.ToStatus == targetStatus
                               && !string.IsNullOrWhiteSpace(activity.StatusMessage))
            .OrderByDescending(activity => activity.OccurredOn)
            .Select(activity => activity.StatusMessage.Trim())
            .FirstOrDefault() ?? string.Empty;
    }

    public void SetStatus(TaskStatus newStatus, string statusMessage = "")
    {
        SetStatus(newStatus, DateTime.Now, statusMessage);
    }

    public void SetStatus(TaskStatus newStatus, DateTime occurredOn, string statusMessage = "")
    {
        var previousStatus = status;
        if (previousStatus == newStatus)
        {
            return;
        }

        if (newStatus == TaskStatus.Active)
        {
            statusBeforeActive = previousStatus;
            OnPropertyChanged(nameof(StatusBeforeActive));
        }

        if (SetProperty(ref status, newStatus, nameof(Status)))
        {
            UpdatedOn = DateTime.Now;
            AddStatusChangeActivity(previousStatus, newStatus, occurredOn, statusMessage);
        }
    }

    public void InitializeStatus(TaskStatus currentStatus, TaskStatus previousStatus)
    {
        status = currentStatus;
        statusBeforeActive = previousStatus;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusBeforeActive));
        OnPropertyChanged(nameof(CurrentStatusMessage));
        OnPropertyChanged(nameof(HasCurrentStatusMessage));
    }

    public void AddActivity(string activity)
    {
        Activities.Add(new TaskActivity
        {
            OccurredOn = DateTime.Now,
            Activity = activity
        });
    }

    public void NotifyActivitiesChanged()
    {
        OnPropertyChanged(nameof(CurrentStatusMessage));
        OnPropertyChanged(nameof(HasCurrentStatusMessage));
    }

    private void AddStatusChangeActivity(TaskStatus fromStatus, TaskStatus toStatus, DateTime occurredOn, string statusMessage)
    {
        Activities.Add(new TaskActivity
        {
            OccurredOn = occurredOn,
            Activity = $"Status changed from {FormatStatus(fromStatus)} to {FormatStatus(toStatus)}.",
            FromStatus = fromStatus,
            ToStatus = toStatus,
            StatusMessage = statusMessage.Trim()
        });
        NotifyActivitiesChanged();
    }

    private static string FormatStatus(TaskStatus taskStatus)
    {
        return taskStatus switch
        {
            TaskStatus.InProgress => "In-Progress",
            TaskStatus.OnHold => "On Hold",
            _ => taskStatus.ToString()
        };
    }
}
