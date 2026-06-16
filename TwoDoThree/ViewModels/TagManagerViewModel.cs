using System.Collections.ObjectModel;
using TwoDoThree.Models;

namespace TwoDoThree.ViewModels;

public sealed class TagManagerViewModel : ObservableObject
{
    private string newTag = string.Empty;
    private string? selectedTag;

    public TagManagerViewModel(IEnumerable<string> tags, string? suggestedTag = null)
    {
        Tags = new ObservableCollection<string>(TagSettings.NormalizeTags(tags));
        NewTag = suggestedTag?.Trim() ?? string.Empty;
        AddTagCommand = new RelayCommand(_ => AddTag(), _ => !string.IsNullOrWhiteSpace(NewTag));
        RemoveTagCommand = new RelayCommand(_ => RemoveSelectedTag(), _ => SelectedTag is not null);
    }

    public ObservableCollection<string> Tags { get; }

    public string NewTag
    {
        get => newTag;
        set
        {
            if (SetProperty(ref newTag, value)
                && AddTagCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedTag
    {
        get => selectedTag;
        set
        {
            if (SetProperty(ref selectedTag, value)
                && RemoveTagCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddTagCommand { get; }

    public RelayCommand RemoveTagCommand { get; }

    public void ApplyTo(TagSettings settings)
    {
        settings.ReplaceTags(Tags);
    }

    private void AddTag()
    {
        var tag = NewTag.Trim();
        if (string.IsNullOrWhiteSpace(tag)
            || Tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
        {
            NewTag = string.Empty;
            return;
        }

        var insertIndex = 0;
        while (insertIndex < Tags.Count
               && string.Compare(Tags[insertIndex], tag, StringComparison.OrdinalIgnoreCase) < 0)
        {
            insertIndex++;
        }

        Tags.Insert(insertIndex, tag);
        SelectedTag = tag;
        NewTag = string.Empty;
    }

    private void RemoveSelectedTag()
    {
        if (SelectedTag is null)
        {
            return;
        }

        var index = Tags.IndexOf(SelectedTag);
        if (index < 0)
        {
            return;
        }

        Tags.RemoveAt(index);
        SelectedTag = Tags.Count == 0
            ? null
            : Tags[Math.Min(index, Tags.Count - 1)];
    }
}
