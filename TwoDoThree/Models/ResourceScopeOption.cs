namespace TwoDoThree.Models;

public enum ResourceScopeKind
{
    ThisTask,
    TagAll,
    TagGlobal
}

public sealed class ResourceScopeOption
{
    private ResourceScopeOption(ResourceScopeKind kind, string tag)
    {
        Kind = kind;
        Tag = tag;
        DisplayName = kind switch
        {
            ResourceScopeKind.TagAll => $"{tag} - ALL",
            ResourceScopeKind.TagGlobal => $"{tag} - Global",
            _ => "This Task"
        };
    }

    public string DisplayName { get; }

    public ResourceScopeKind Kind { get; }

    public string Tag { get; }

    public bool IsThisTask => Kind == ResourceScopeKind.ThisTask;

    public bool IsTagAll => Kind == ResourceScopeKind.TagAll;

    public bool IsTagGlobal => Kind == ResourceScopeKind.TagGlobal;

    public static ResourceScopeOption ThisTask { get; } = new(ResourceScopeKind.ThisTask, string.Empty);

    public static ResourceScopeOption ForTagAll(string tag)
    {
        return new ResourceScopeOption(ResourceScopeKind.TagAll, tag);
    }

    public static ResourceScopeOption ForTagGlobal(string tag)
    {
        return new ResourceScopeOption(ResourceScopeKind.TagGlobal, tag);
    }
}
