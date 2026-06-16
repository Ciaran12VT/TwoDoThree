using System.Collections.ObjectModel;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class TaskItem : ObservableObject
{
    private int id;
    private string title = string.Empty;
    private string tags = string.Empty;
    private DateTime createdOn = DateTime.Now;
    private DateTime updatedOn = DateTime.Now;
    private TimeSpan timeSpent;

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

    public ObservableCollection<ResourceItem> Resources { get; } = new();

    public ObservableCollection<ActionItem> Actions { get; } = new();
}
