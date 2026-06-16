using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class ActionItem : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string actionNumber = string.Empty;
    private string actionText = string.Empty;
    private int indentLevel;
    private ActionStatus status;

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string ActionNumber
    {
        get => actionNumber;
        set => SetProperty(ref actionNumber, value);
    }

    public string ActionText
    {
        get => actionText;
        set => SetProperty(ref actionText, value);
    }

    public int IndentLevel
    {
        get => indentLevel;
        set => SetProperty(ref indentLevel, Math.Max(0, value));
    }

    public ActionStatus Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }
}
