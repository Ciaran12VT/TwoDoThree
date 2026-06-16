using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class ResourceItem : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string name = string.Empty;
    private ResourceKind kind;
    private string content = string.Empty;
    private string formattedContent = string.Empty;
    private string codeLanguage = "C#";
    private string emailMessageId = string.Empty;
    private string emailFrom = string.Empty;
    private string emailSubject = string.Empty;
    private DateTime? emailReceivedOn;

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public ResourceKind Kind
    {
        get => kind;
        set => SetProperty(ref kind, value);
    }

    public string Content
    {
        get => content;
        set => SetProperty(ref content, value);
    }

    public string FormattedContent
    {
        get => formattedContent;
        set => SetProperty(ref formattedContent, value);
    }

    public string CodeLanguage
    {
        get => codeLanguage;
        set => SetProperty(ref codeLanguage, value);
    }

    public string EmailMessageId
    {
        get => emailMessageId;
        set => SetProperty(ref emailMessageId, value);
    }

    public string EmailFrom
    {
        get => emailFrom;
        set => SetProperty(ref emailFrom, value);
    }

    public string EmailSubject
    {
        get => emailSubject;
        set => SetProperty(ref emailSubject, value);
    }

    public DateTime? EmailReceivedOn
    {
        get => emailReceivedOn;
        set => SetProperty(ref emailReceivedOn, value);
    }
}
