using TwoDoThree.Models;

namespace TwoDoThree.Services;

public static class ResourceHierarchy
{
    // Resources remain flat in persistence. Only the tree projection is nested.
    public static IReadOnlyList<ResourceItem> Build(IEnumerable<ResourceItem> resources, string filter)
    {
        var items = resources.Where(resource => resource.IsFileTreeResource).ToList();
        var folders = items.Where(resource => resource.Kind == ResourceKind.VirtualFolder)
            .ToDictionary(resource => resource.Id);
        var byId = items.ToDictionary(resource => resource.Id);
        foreach (var resource in items) resource.ParentVirtualFolderId = null;
        foreach (var folder in folders.Values)
        {
            foreach (var id in VirtualFolderData.Read(folder).ChildResourceIds)
            {
                if (byId.TryGetValue(id, out var child) && child.ParentVirtualFolderId is null)
                    child.ParentVirtualFolderId = folder.Id;
            }
        }
        var children = new Dictionary<Guid, List<ResourceItem>>();
        var roots = new List<ResourceItem>();
        foreach (var resource in items)
        {
            resource.VirtualChildren.Clear();
            var seen = new HashSet<Guid> { resource.Id };
            var parentId = resource.ParentVirtualFolderId;
            var valid = parentId.HasValue && folders.ContainsKey(parentId.Value);
            while (valid && parentId is Guid id && folders.TryGetValue(id, out var parent))
            {
                if (!seen.Add(id)) { valid = false; break; }
                parentId = parent.ParentVirtualFolderId;
            }

            if (valid)
            {
                var id = resource.ParentVirtualFolderId!.Value;
                if (!children.TryGetValue(id, out var siblings)) children[id] = siblings = [];
                siblings.Add(resource);
            }
            else roots.Add(resource); // Keep orphaned or cyclic records visible and recoverable.
        }

        bool Populate(ResourceItem resource, bool ancestorMatches)
        {
            var matches = ancestorMatches || string.IsNullOrWhiteSpace(filter)
                || resource.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
            if (children.TryGetValue(resource.Id, out var descendants))
            {
                foreach (var child in descendants)
                    if (Populate(child, matches)) resource.VirtualChildren.Add(child);
            }

            if (!string.IsNullOrWhiteSpace(filter) && resource.VirtualChildren.Count > 0)
                resource.IsExpanded = true;
            return matches || resource.VirtualChildren.Count > 0;
        }

        return roots.Where(resource => Populate(resource, false)).ToList();
    }
}
