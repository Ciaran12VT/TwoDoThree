using System.Collections.ObjectModel;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class TaskListSettings : ObservableObject
{
    private string selectedFilterSetId = string.Empty;

    public static IReadOnlyList<TaskListColumn> DefaultVisibleColumns { get; } =
    [
        TaskListColumn.Id,
        TaskListColumn.Title,
        TaskListColumn.Tags,
        TaskListColumn.Pocs,
        TaskListColumn.Status,
        TaskListColumn.DueBy,
        TaskListColumn.CreatedOn,
        TaskListColumn.UpdatedOn,
        TaskListColumn.TimeSpent
    ];

    public TaskListSettings()
    {
        ReplaceVisibleColumns(DefaultVisibleColumns);
    }

    public ObservableCollection<TaskListColumn> VisibleColumns { get; } = new();

    public ObservableCollection<TaskFilterSet> FilterSets { get; } = new();

    public string SelectedFilterSetId
    {
        get => selectedFilterSetId;
        set => SetProperty(ref selectedFilterSetId, value ?? string.Empty);
    }

    public bool IsColumnVisible(TaskListColumn column)
    {
        return VisibleColumns.Contains(column);
    }

    public void SetColumnVisible(TaskListColumn column, bool isVisible)
    {
        if (isVisible)
        {
            if (!VisibleColumns.Contains(column))
            {
                VisibleColumns.Add(column);
            }

            return;
        }

        VisibleColumns.Remove(column);
    }

    public void ReplaceVisibleColumns(IEnumerable<TaskListColumn> columns)
    {
        VisibleColumns.Clear();
        foreach (var column in columns.Distinct())
        {
            VisibleColumns.Add(column);
        }
    }

    public void ReplaceFilterSets(IEnumerable<TaskFilterSet> filterSets)
    {
        FilterSets.Clear();
        foreach (var filterSet in filterSets)
        {
            FilterSets.Add(filterSet.Clone());
        }
    }
}
