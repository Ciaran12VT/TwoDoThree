using System.Collections.ObjectModel;

namespace TwoDoThree.Models;

public sealed class ResourceGroup
{
    public ResourceGroup(ResourceKind kind)
    {
        Kind = kind;
    }

    public ResourceKind Kind { get; }

    public string Name => Kind switch
    {
        ResourceKind.Email => "Emails",
        ResourceKind.Sheet => "Sheets",
        ResourceKind.CodeSnippet => "Code Snippets",
        ResourceKind.SurfResource => "Surf Resources",
        ResourceKind.File => "Files and Folders",
        _ => $"{Kind}s"
    };

    public ObservableCollection<ResourceItem> Resources { get; } = new();
}
