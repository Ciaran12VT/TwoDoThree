using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class EmailMessage : ObservableObject
{
    private string associatedTaskNames = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public DateTime ReceivedOn { get; init; }

    public string Preview { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string DisplayTitle => $"{From} - {Subject}";

    public string AssociatedTaskNames
    {
        get => associatedTaskNames;
        set
        {
            if (SetProperty(ref associatedTaskNames, value))
            {
                OnPropertyChanged(nameof(AssociatedTaskSummary));
                OnPropertyChanged(nameof(HasAssociatedTasks));
            }
        }
    }

    public string AssociatedTaskSummary
    {
        get
        {
            if (!HasAssociatedTasks)
            {
                return string.Empty;
            }

            return AssociatedTaskNames.Contains(',', StringComparison.Ordinal)
                ? $"Tasks: {AssociatedTaskNames}"
                : $"Task: {AssociatedTaskNames}";
        }
    }

    public bool HasAssociatedTasks => !string.IsNullOrWhiteSpace(AssociatedTaskNames);
}
