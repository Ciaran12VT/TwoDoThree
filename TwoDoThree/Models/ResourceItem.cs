using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class ResourceItem : ObservableObject
{
    private string name = string.Empty;
    private ResourceKind kind;
    private string content = string.Empty;
    private string codeLanguage = "C#";

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

    public string CodeLanguage
    {
        get => codeLanguage;
        set => SetProperty(ref codeLanguage, value);
    }
}
