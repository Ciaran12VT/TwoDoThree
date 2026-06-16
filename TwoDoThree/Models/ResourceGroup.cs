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
        ResourceKind.CodeSnippet => "Code Snippets",
        ResourceKind.SurfResource => "Surf Resources",
        _ => $"{Kind}s"
    };

    public ObservableCollection<ResourceItem> Resources { get; } = new();
}
