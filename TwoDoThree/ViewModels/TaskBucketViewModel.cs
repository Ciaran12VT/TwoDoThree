using System.Collections.ObjectModel;
using TwoDoThree.Models;

namespace TwoDoThree.ViewModels;

public sealed class TaskBucketViewModel
{
    public TaskBucketViewModel(string title, object key, IEnumerable<TaskItem> tasks)
    {
        Title = title;
        Key = key;
        Tasks = new ObservableCollection<TaskItem>(tasks);
    }

    public string Title { get; }

    public object Key { get; }

    public ObservableCollection<TaskItem> Tasks { get; }
}
