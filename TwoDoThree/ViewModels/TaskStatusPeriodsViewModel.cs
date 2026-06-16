using System.Collections.ObjectModel;
using TwoDoThree.Models;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.ViewModels;

public sealed class TaskStatusPeriodsViewModel
{
    public TaskStatusPeriodsViewModel(TaskItem task)
    {
        Task = task;
        Periods = new ObservableCollection<TaskStatusPeriod>(BuildPeriods(task, DateTime.Now));
    }

    public TaskItem Task { get; }

    public ObservableCollection<TaskStatusPeriod> Periods { get; }

    private static IEnumerable<TaskStatusPeriod> BuildPeriods(TaskItem task, DateTime now)
    {
        var currentStatus = TaskItemStatus.Inactive;
        var currentStatusMessage = string.Empty;
        var periodStart = task.CreatedOn;

        foreach (var activity in task.Activities
                     .Where(activity => activity.ToStatus.HasValue)
                     .OrderBy(activity => activity.OccurredOn))
        {
            var nextStatus = activity.ToStatus.GetValueOrDefault();

            if (activity.OccurredOn > periodStart)
            {
                yield return new TaskStatusPeriod
                {
                    Status = currentStatus,
                    StatusMessage = currentStatusMessage,
                    StartTime = periodStart,
                    EndTime = activity.OccurredOn
                };
            }

            currentStatus = nextStatus;
            currentStatusMessage = activity.StatusMessage;
            periodStart = activity.OccurredOn;
        }

        if (now >= periodStart)
        {
            yield return new TaskStatusPeriod
            {
                Status = currentStatus,
                StatusMessage = currentStatusMessage,
                StartTime = periodStart,
                EndTime = now,
                IsCurrent = task.Status == currentStatus
            };
        }
    }
}
