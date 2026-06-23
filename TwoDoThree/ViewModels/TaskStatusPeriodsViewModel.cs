using System.Collections.ObjectModel;
using TwoDoThree.Models;
using TwoDoThree.Services;
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

    public bool TryUpdatePeriod(
        TaskStatusPeriod period,
        DateTime startTime,
        DateTime endTime,
        string statusMessage,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (period.CanEditEndTime && endTime < startTime)
        {
            errorMessage = "End time must be after start time.";
            return false;
        }

        if (!ValidateStartBoundary(period, startTime, out errorMessage)
            || !ValidateEndBoundary(period, endTime, out errorMessage))
        {
            return false;
        }

        if (period.StartActivity is null)
        {
            Task.CreatedOn = startTime;
        }
        else
        {
            period.StartActivity.OccurredOn = startTime;
            period.StartActivity.StatusMessage = statusMessage.Trim();
        }

        if (period.EndActivity is not null)
        {
            period.EndActivity.OccurredOn = endTime;
        }

        Task.NotifyActivitiesChanged();
        TaskTimeCalculator.UpdateTimeSpent(Task);
        Task.UpdatedOn = DateTime.Now;
        RefreshPeriods();
        return true;
    }

    public void RefreshPeriods()
    {
        Periods.Clear();
        foreach (var period in BuildPeriods(Task, DateTime.Now))
        {
            Periods.Add(period);
        }
    }

    private static IEnumerable<TaskStatusPeriod> BuildPeriods(TaskItem task, DateTime now)
    {
        var currentStatus = TaskItemStatus.Inactive;
        var currentStatusMessage = string.Empty;
        var periodStart = task.CreatedOn;
        TaskActivity? periodStartActivity = null;

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
                    EndTime = activity.OccurredOn,
                    StartActivity = periodStartActivity,
                    EndActivity = activity
                };
            }

            currentStatus = nextStatus;
            currentStatusMessage = activity.StatusMessage;
            periodStart = activity.OccurredOn;
            periodStartActivity = activity;
        }

        if (now >= periodStart)
        {
            yield return new TaskStatusPeriod
            {
                Status = currentStatus,
                StatusMessage = currentStatusMessage,
                StartTime = periodStart,
                EndTime = now,
                IsCurrent = task.Status == currentStatus,
                StartActivity = periodStartActivity
            };
        }
    }

    private bool ValidateStartBoundary(TaskStatusPeriod period, DateTime startTime, out string errorMessage)
    {
        errorMessage = string.Empty;
        var earliestStart = period.StartActivity is null
            ? DateTime.MinValue
            : GetPreviousStatusChange(period.StartActivity)?.OccurredOn ?? Task.CreatedOn;
        var latestStart = period.EndActivity?.OccurredOn ?? DateTime.Now;

        if (startTime < earliestStart)
        {
            errorMessage = "Start time cannot be before the previous status change.";
            return false;
        }

        if (startTime > latestStart)
        {
            errorMessage = "Start time cannot be after the period end time.";
            return false;
        }

        return true;
    }

    private bool ValidateEndBoundary(TaskStatusPeriod period, DateTime endTime, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (period.EndActivity is null)
        {
            return true;
        }

        var earliestEnd = period.StartActivity?.OccurredOn ?? Task.CreatedOn;
        var latestEnd = GetNextStatusChange(period.EndActivity)?.OccurredOn ?? DateTime.Now;

        if (endTime < earliestEnd)
        {
            errorMessage = "End time cannot be before the period start time.";
            return false;
        }

        if (endTime > latestEnd)
        {
            errorMessage = "End time cannot be after the next status change.";
            return false;
        }

        return true;
    }

    private TaskActivity? GetPreviousStatusChange(TaskActivity? activity)
    {
        if (activity is null)
        {
            return null;
        }

        return Task.Activities
            .Where(candidate => candidate.ToStatus.HasValue
                                && !ReferenceEquals(candidate, activity)
                                && candidate.OccurredOn <= activity.OccurredOn)
            .OrderByDescending(candidate => candidate.OccurredOn)
            .FirstOrDefault();
    }

    private TaskActivity? GetNextStatusChange(TaskActivity activity)
    {
        return Task.Activities
            .Where(candidate => candidate.ToStatus.HasValue
                                && !ReferenceEquals(candidate, activity)
                                && candidate.OccurredOn >= activity.OccurredOn)
            .OrderBy(candidate => candidate.OccurredOn)
            .FirstOrDefault();
    }
}
