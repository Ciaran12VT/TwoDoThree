using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface ITaskStore
{
    bool IsConfigured { get; }

    IReadOnlyList<TaskItem> LoadTasks();

    IReadOnlyList<TagResourceCollection> LoadTagResources();

    void SaveTask(TaskItem task);

    void SaveTagResource(string tag, ResourceItem resource, int sortOrder);

    void DeleteTask(int taskId);
}
