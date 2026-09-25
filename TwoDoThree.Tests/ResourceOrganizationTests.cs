using System.IO;
using System.Text.Json;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Tests;

public class ResourceOrganizationTests
{
    [Fact]
    public void NestedGroupsSurviveSerializationAndKeepFilesInOnePlace()
    {
        var task = new TaskItem();
        var model = CreateDetail(task);
        var root = model.CreateVirtualFolder("Project")!;
        var nested = model.CreateVirtualFolder("Documents", root)!;
        var file = new ResourceItem { Kind = ResourceKind.File, Name = "notes.md", Content = @"C:\notes.md" };
        task.Resources.Add(file);

        Assert.True(model.MoveResource(file, nested));

        var saved = JsonSerializer.Serialize(task.Resources);
        Assert.DoesNotContain("VirtualChildren", saved);
        var reloaded = JsonSerializer.Deserialize<List<ResourceItem>>(saved)!;
        var roots = ResourceHierarchy.Build(reloaded, "");
        var reloadedRoot = Assert.Single(roots);
        Assert.Equal(root.Id, reloadedRoot.Id);
        var reloadedNested = Assert.Single(reloadedRoot.VirtualChildren);
        Assert.Equal(nested.Id, reloadedNested.Id);
        Assert.Equal(file.Id, Assert.Single(reloadedNested.VirtualChildren).Id);
        Assert.Equal(nested.Id, reloaded.Single(item => item.Id == file.Id).ParentVirtualFolderId);
    }

    [Fact]
    public void MoveRejectsCyclesOtherTasksAndNonFileResources()
    {
        var task = new TaskItem();
        var model = CreateDetail(task);
        var parent = model.CreateVirtualFolder("Parent")!;
        var child = model.CreateVirtualFolder("Child", parent)!;
        var grandchild = model.CreateVirtualFolder("Grandchild", child)!;
        var text = new ResourceItem { Kind = ResourceKind.Text };
        var external = new ResourceItem { Kind = ResourceKind.File };
        var foreignFolder = new ResourceItem { Kind = ResourceKind.VirtualFolder };
        task.Resources.Add(text);

        Assert.False(model.MoveResource(parent, parent));
        Assert.False(model.MoveResource(parent, grandchild));
        Assert.False(model.MoveResource(child, grandchild));
        Assert.False(model.MoveResource(text, parent));
        Assert.False(model.MoveResource(external, parent));
        Assert.False(model.MoveResource(child, foreignFolder));
        Assert.Null(model.CreateVirtualFolder("Invalid", foreignFolder));
        Assert.Null(parent.ParentVirtualFolderId);
        Assert.Equal(parent.Id, child.ParentVirtualFolderId);
    }

    [Fact]
    public void RemovingVirtualFolderPromotesChildrenWithoutRemovingResources()
    {
        var task = new TaskItem();
        var model = CreateDetail(task);
        var root = model.CreateVirtualFolder("Root")!;
        var removed = model.CreateVirtualFolder("Remove", root)!;
        var nested = model.CreateVirtualFolder("Keep", removed)!;
        var file = new ResourceItem { Kind = ResourceKind.File, ParentVirtualFolderId = removed.Id, Content = @"C:\keep.txt" };
        task.Resources.Add(file);
        model.MoveResource(file, removed);

        Assert.True(model.RemoveResourceFromTask(removed));

        Assert.Equal(3, task.Resources.Count);
        Assert.Equal(root.Id, nested.ParentVirtualFolderId);
        Assert.Equal(root.Id, file.ParentVirtualFolderId);
        Assert.Contains(file, root.VirtualChildren);
        Assert.Equal(@"C:\keep.txt", file.Content);
        Assert.True(model.RemoveResourceFromTask(root));
        Assert.Null(nested.ParentVirtualFolderId);
        Assert.Null(file.ParentVirtualFolderId);
    }

    [Fact]
    public void SearchKeepsAncestorsOfMatchingFilesAndShowsContentsOfMatchingGroups()
    {
        var task = new TaskItem();
        var model = CreateDetail(task);
        var root = model.CreateVirtualFolder("Project")!;
        var nested = model.CreateVirtualFolder("Docs", root)!;
        var match = new ResourceItem { Kind = ResourceKind.File, Name = "needle.md", ParentVirtualFolderId = nested.Id };
        var other = new ResourceItem { Kind = ResourceKind.File, Name = "other.txt", ParentVirtualFolderId = nested.Id };
        task.Resources.Add(match);
        task.Resources.Add(other);
        model.MoveResource(match, nested);
        model.MoveResource(other, nested);

        var roots = ResourceHierarchy.Build(task.Resources, "needle");
        Assert.Same(root, Assert.Single(roots));
        Assert.Same(nested, Assert.Single(root.VirtualChildren));
        Assert.Same(match, Assert.Single(nested.VirtualChildren));
        Assert.True(root.IsExpanded);
        ResourceHierarchy.Build(task.Resources, "Project");
        Assert.Equal(2, nested.VirtualChildren.Count);
        Assert.Empty(ResourceHierarchy.Build(task.Resources, "missing"));
    }

    [Fact]
    public void CorruptHierarchyDoesNotHideResourcesOrLoop()
    {
        var first = new ResourceItem { Kind = ResourceKind.VirtualFolder };
        var second = new ResourceItem { Kind = ResourceKind.VirtualFolder, ParentVirtualFolderId = first.Id };
        first.ParentVirtualFolderId = second.Id;
        new VirtualFolderData { ChildResourceIds = [second.Id] }.Save(first);
        new VirtualFolderData { ChildResourceIds = [first.Id] }.Save(second);
        var orphan = new ResourceItem { Kind = ResourceKind.File, ParentVirtualFolderId = Guid.NewGuid() };
        var roots = ResourceHierarchy.Build([first, second, orphan], "");
        Assert.Equal(3, roots.Count);
        Assert.All(roots, resource => Assert.Empty(resource.VirtualChildren));
    }

    [Fact]
    public void DroppedPathsBecomeLinksInFilesAndFoldersWithoutChangingDisk()
    {
        var directory = Directory.CreateTempSubdirectory("2do3-drops-");
        var path = Path.Combine(directory.FullName, "picture.png");
        File.WriteAllText(path, "unchanged");
        try
        {
            var task = new TaskItem { Tags = "Shared" };
            var model = CreateDetail(task);
            var group = model.CreateVirtualFolder(Path.Combine(directory.FullName, "virtual-only"))!;
            model.SelectedResourceScope = ResourceScopeOption.ForTagGlobal("Shared");
            model.ResourceSearchText = "no match";

            var dropped = model.AddLocalPaths([path, path.ToUpperInvariant(), directory.FullName,
                Path.Combine(directory.FullName, "missing.txt"), "relative.txt", "https://example.com/a.txt"], group);

            Assert.Equal(2, dropped.Count);
            Assert.Equal(ResourceKind.File, dropped[0].Kind); // Images dropped here are linked files too.
            Assert.Equal(ResourceKind.Folder, dropped[1].Kind);
            Assert.All(dropped, item => Assert.Equal(group.Id, item.ParentVirtualFolderId));
            Assert.Equal(3, task.Resources.Count);
            Assert.True(model.SelectedResourceScope.IsThisTask);
            Assert.Equal("", model.ResourceSearchText);
            Assert.Same(dropped[1], model.SelectedResource);
            Assert.Equal("unchanged", File.ReadAllText(path));
            Assert.False(Directory.Exists(group.Name));
            Assert.True(model.MoveResource(dropped[0], null));
            Assert.Null(dropped[0].ParentVirtualFolderId);
            Assert.Equal("unchanged", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
            directory.Delete();
        }
    }

    private static TaskDetailViewModel CreateDetail(TaskItem task) =>
        new(task, [task], [], (_, _) => null, new TagSettings(), new Surf2IntegrationSettings(),
            new Surf2IntegrationService(), new Surf2Launcher());
}
