using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using TwoDoThree.Models;

namespace TwoDoThree.ViewModels;

public sealed class TaskDetailViewModel : ObservableObject
{
    private ResourceItem? selectedResource;
    private ActionItem? selectedAction;

    public TaskDetailViewModel(TaskItem task)
    {
        Task = task;
        AddTextResourceCommand = new RelayCommand(_ => AddTextResource());
        AddCodeResourceCommand = new RelayCommand(_ => AddCodeResource());
        AddImageResourceCommand = new RelayCommand(_ => AddFileResource(ResourceKind.Image));
        AddAudioResourceCommand = new RelayCommand(_ => AddFileResource(ResourceKind.Audio));
        AddSurfResourceCommand = new RelayCommand(_ => AddSurfResource());
        AddActionCommand = new RelayCommand(_ => AddAction());

        RebuildResourceGroups();
        SelectedResource = Task.Resources.FirstOrDefault();
        RenumberActions(Task.Actions);
    }

    public TaskItem Task { get; }

    public IReadOnlyList<TwoDoThree.Models.TaskStatus> TaskStatusValues { get; } = Enum.GetValues<TwoDoThree.Models.TaskStatus>();

    public ObservableCollection<ActionItem> Actions => Task.Actions;

    public ObservableCollection<ResourceGroup> ResourceGroups { get; } = new();

    public ActionItem? SelectedAction
    {
        get => selectedAction;
        set => SetProperty(ref selectedAction, value);
    }

    public ResourceItem? SelectedResource
    {
        get => selectedResource;
        set => SetProperty(ref selectedResource, value);
    }

    public ICommand AddTextResourceCommand { get; }

    public ICommand AddCodeResourceCommand { get; }

    public ICommand AddImageResourceCommand { get; }

    public ICommand AddAudioResourceCommand { get; }

    public ICommand AddSurfResourceCommand { get; }

    public ICommand AddActionCommand { get; }

    public static void RenumberActions(IEnumerable<ActionItem> actions)
    {
        var counters = new List<int>();
        var previousIndent = 0;
        var isFirst = true;

        foreach (var action in actions)
        {
            var indent = action.IndentLevel;
            if (isFirst)
            {
                indent = 0;
            }
            else
            {
                indent = Math.Min(indent, previousIndent + 1);
            }

            action.IndentLevel = indent;

            while (counters.Count <= indent)
            {
                counters.Add(0);
            }

            counters[indent]++;

            if (counters.Count > indent + 1)
            {
                counters.RemoveRange(indent + 1, counters.Count - indent - 1);
            }

            action.ActionNumber = string.Join(".", counters.Take(indent + 1));
            previousIndent = indent;
            isFirst = false;
        }
    }

    public void IndentAction(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index <= 0)
        {
            return;
        }

        var maxIndent = Actions[index - 1].IndentLevel + 1;
        if (action.IndentLevel < maxIndent)
        {
            action.IndentLevel++;
            TouchTask();
            RenumberActions(Actions);
        }
    }

    public void UnindentAction(ActionItem action)
    {
        if (action.IndentLevel == 0)
        {
            return;
        }

        action.IndentLevel--;
        TouchTask();
        RenumberActions(Actions);
    }

    public void MoveActionUp(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index <= 0)
        {
            return;
        }

        Actions.Move(index, index - 1);
        TouchTask();
        RenumberActions(Actions);
    }

    public void MoveActionDown(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index < 0 || index >= Actions.Count - 1)
        {
            return;
        }

        Actions.Move(index, index + 1);
        TouchTask();
        RenumberActions(Actions);
    }

    public void SetActionStatus(ActionItem action, ActionStatus status)
    {
        action.Status = status;
        TouchTask();
    }

    public void DeleteAction(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index < 0)
        {
            return;
        }

        Actions.RemoveAt(index);
        TouchTask();
        RenumberActions(Actions);
        SelectedAction = Actions.Count == 0
            ? null
            : Actions[Math.Min(index, Actions.Count - 1)];
    }

    public void AddActionBelow(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index < 0)
        {
            return;
        }

        var newAction = CreateAction(action.IndentLevel);
        Actions.Insert(index + 1, newAction);
        SelectedAction = newAction;
        TouchTask();
        RenumberActions(Actions);
    }

    public void CycleActionStatus(ActionItem action, int direction)
    {
        var statuses = Enum.GetValues<ActionStatus>();
        var index = Array.IndexOf(statuses, action.Status);
        if (index < 0)
        {
            index = 0;
        }

        var nextIndex = (index + direction + statuses.Length) % statuses.Length;
        SetActionStatus(action, statuses[nextIndex]);
    }

    private void AddAction()
    {
        if (SelectedAction is { } selected)
        {
            AddChildAction(selected);
            return;
        }

        var action = CreateAction(0);
        Actions.Add(action);
        SelectedAction = action;
        TouchTask();
        RenumberActions(Actions);
    }

    private void AddChildAction(ActionItem parent)
    {
        var index = Actions.IndexOf(parent);
        if (index < 0)
        {
            return;
        }

        var action = CreateAction(parent.IndentLevel + 1);
        Actions.Insert(index + 1, action);
        SelectedAction = action;
        TouchTask();
        RenumberActions(Actions);
    }

    private static ActionItem CreateAction(int indentLevel)
    {
        return new ActionItem
        {
            ActionText = "New action",
            IndentLevel = indentLevel,
            Status = ActionStatus.NotStarted
        };
    }

    private void AddTextResource()
    {
        AddResource(new ResourceItem
        {
            Name = $"Text note {Task.Resources.Count(r => r.Kind == ResourceKind.Text) + 1}",
            Kind = ResourceKind.Text,
            Content = "Write notes for this task here."
        });
    }

    private void AddCodeResource()
    {
        AddResource(new ResourceItem
        {
            Name = $"Code snippet {Task.Resources.Count(r => r.Kind == ResourceKind.CodeSnippet) + 1}",
            Kind = ResourceKind.CodeSnippet,
            Content = "// Paste or type a useful code snippet here."
        });
    }

    private void AddFileResource(ResourceKind kind)
    {
        var dialog = new OpenFileDialog
        {
            Title = kind == ResourceKind.Image ? "Select image resource" : "Select audio resource",
            Filter = kind == ResourceKind.Image
                ? "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*"
                : "Audio files|*.mp3;*.wav;*.wma;*.m4a|All files|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddResource(new ResourceItem
        {
            Name = Path.GetFileName(dialog.FileName),
            Kind = kind,
            Content = dialog.FileName
        });
    }

    private void AddSurfResource()
    {
        AddResource(new ResourceItem
        {
            Name = $"Surf resource {Task.Resources.Count(r => r.Kind == ResourceKind.SurfResource) + 1}",
            Kind = ResourceKind.SurfResource,
            Content = "Surf resource placeholder"
        });
    }

    private void AddResource(ResourceItem resource)
    {
        Task.Resources.Add(resource);
        RebuildResourceGroups();
        SelectedResource = resource;
        TouchTask();
    }

    private void RebuildResourceGroups()
    {
        ResourceGroups.Clear();

        foreach (var kind in Enum.GetValues<ResourceKind>())
        {
            var group = new ResourceGroup(kind);
            foreach (var resource in Task.Resources.Where(resource => resource.Kind == kind))
            {
                group.Resources.Add(resource);
            }

            ResourceGroups.Add(group);
        }
    }

    private void TouchTask()
    {
        Task.UpdatedOn = DateTime.Now;
    }
}
