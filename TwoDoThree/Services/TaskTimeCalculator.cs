using TwoDoThree.Models;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Services;

public static class TaskTimeCalculator
{
    public static void UpdateTimeSpent(TaskItem task)
    {
        UpdateTimeSpent(task, DateTime.Now);
    }

    public static void UpdateTimeSpent(TaskItem task, DateTime now)
    {
        task.TimeSpent = CalculateTimeSpent(task, now);
    }

    public static TimeSpan CalculateTimeSpent(TaskItem task, DateTime now)
    {
        var total = TimeSpan.Zero;
        DateTime? activeStartedOn = null;

        foreach (var activity in task.Activities
                     .Where(activity => activity.FromStatus.HasValue || activity.ToStatus.HasValue)
                     .OrderBy(activity => activity.OccurredOn))
        {
            if (activity.ToStatus == TaskItemStatus.Active)
            {
                activeStartedOn = activity.OccurredOn;
            }
            else if (activity.FromStatus == TaskItemStatus.Active && activeStartedOn.HasValue)
            {
                total += activity.OccurredOn - activeStartedOn.Value;
                activeStartedOn = null;
            }
        }

        if (task.Status == TaskItemStatus.Active && activeStartedOn.HasValue)
        {
            total += now - activeStartedOn.Value;
        }

        return TimeSpan.FromSeconds(Math.Max(0, Math.Floor(total.TotalSeconds)));
    }
}
