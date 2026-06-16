using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface ITaskStore
{
    bool IsConfigured { get; }

    IReadOnlyList<TaskItem> LoadTasks();

    void SaveTask(TaskItem task);
}
