using System.Text.Json;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Tests;

public class ResourceRemovalTests
{
    public static IEnumerable<object[]> ResourceKinds => Enum.GetValues<ResourceKind>().Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(ResourceKinds))]
    public void RemovesEveryAttachedKindAndSelectsRemainingResource(ResourceKind kind)
    {
        var removed = new ResourceItem { Kind = kind, Content = "unique internally stored content", FormattedContent = "unique rich text" };
        var remaining = new ResourceItem { Kind = ResourceKind.Text, Name = "Keep" };
        var task = new TaskItem();
        task.Resources.Add(removed);
        task.Resources.Add(remaining);
        var model = Create(task);
        model.SelectedResource = removed;

        Assert.True(model.CanRemoveResourceFromTask(removed));
        Assert.True(model.RemoveResourceFromTask(removed));
        Assert.Same(remaining, Assert.Single(task.Resources));
        Assert.Same(remaining, model.SelectedResource);
        Assert.DoesNotContain(removed, model.ResourceGroups.SelectMany(group => group.Resources));
        // Internal content is owned by the removed record, and no longer belongs to saved task data.
        Assert.DoesNotContain("unique internally stored content", JsonSerializer.Serialize(task));
        Assert.DoesNotContain("unique rich text", JsonSerializer.Serialize(task));
        Assert.False(model.RemoveResourceFromTask(removed));
    }

    [Fact]
    public void RemovingLastResourceClearsPreview()
    {
        var resource = new ResourceItem();
        var task = new TaskItem();
        task.Resources.Add(resource);
        var model = Create(task);
        Assert.True(model.RemoveResourceFromTask(resource));
        Assert.Null(model.SelectedResource);
        Assert.Empty(task.Resources);
    }

    [Fact]
    public void OtherTasksAndGlobalResourcesCannotBeRemovedThroughCurrentTask()
    {
        var task = new TaskItem { Tags = "review" };
        var other = new TaskItem { Tags = "review" };
        var resource = new ResourceItem { Content = "keep this shared content" };
        other.Resources.Add(resource);
        var global = new TagResourceCollection { Tag = "review" };
        global.Resources.Add(resource);
        var model = Create(task, [task, other], [global]);
        model.SelectedResourceScope = ResourceScopeOption.ForTagAll("review");
        model.RefreshResourceGroups();
        Assert.False(model.CanRemoveResourceFromTask(resource));
        Assert.False(model.RemoveResourceFromTask(resource));
        model.SelectedResourceScope = ResourceScopeOption.ForTagGlobal("review");
        Assert.False(model.RemoveResourceFromTask(resource));
        Assert.Same(resource, Assert.Single(other.Resources));
        Assert.Same(resource, Assert.Single(global.Resources));
        Assert.Equal("keep this shared content", resource.Content);
    }

    [Fact]
    public void RemovingLinkedFileAndFolderPreservesExternalContents()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "external file must remain unchanged");
            var task = new TaskItem();
            var file = new ResourceItem { Kind = ResourceKind.File, Content = path };
            var folder = new ResourceItem { Kind = ResourceKind.Folder, Content = Path.GetDirectoryName(path)! };
            task.Resources.Add(file);
            task.Resources.Add(folder);
            var model = Create(task);
            Assert.True(model.RemoveResourceFromTask(file));
            Assert.True(model.RemoveResourceFromTask(folder));
            Assert.True(Directory.Exists(folder.Content));
            Assert.Equal("external file must remain unchanged", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static TaskDetailViewModel Create(TaskItem task, IEnumerable<TaskItem>? tasks = null,
        IEnumerable<TagResourceCollection>? globals = null) => new(task, tasks ?? [task], globals ?? [],
            (_, _) => null, new TagSettings(), new Surf2IntegrationSettings(), null!, null!);
}
