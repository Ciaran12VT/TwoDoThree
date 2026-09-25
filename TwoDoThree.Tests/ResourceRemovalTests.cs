using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Tests;

public class ResourceRemovalTests
{
    public static IEnumerable<object[]> ResourceKinds =>
        Enum.GetValues<ResourceKind>().Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(ResourceKinds))]
    public void RemovalUpdatesTreeSelectionAndPersistsWithoutResource(ResourceKind kind)
    {
        RunOnStaThread(() =>
        {
            var removed = new ResourceItem
            {
                Name = "Remove me", Kind = kind, Content = "stored content", FormattedContent = "stored formatting"
            };
            var retained = new ResourceItem { Name = "Keep me", Kind = ResourceKind.Text, Content = "keep this" };
            var task = new TaskItem { Id = 1 };
            task.Resources.Add(removed);
            task.Resources.Add(retained);
            var store = new SnapshotTaskStore(task);
            var main = new MainViewModel(new AppSettings(), new EmptyEmailProvider(), null!, null!, store);
            main.FlushPendingTaskSaves();
            store.Snapshot = null;
            var detail = CreateDetail(task);
            detail.SelectedResource = removed;

            Assert.True(detail.CanRemoveResourceFromTask(removed));
            Assert.True(detail.RemoveResourceFromTask(removed));
            Assert.Same(retained, Assert.Single(task.Resources));
            Assert.DoesNotContain(removed, detail.ResourceGroups.SelectMany(group => group.Resources));
            Assert.Same(retained, detail.SelectedResource);

            main.FlushPendingTaskSaves();
            Assert.NotNull(store.Snapshot);
            using var savedTask = JsonDocument.Parse(store.Snapshot);
            var savedResource = Assert.Single(savedTask.RootElement.GetProperty("Resources").EnumerateArray());
            Assert.Equal(retained.Id, savedResource.GetProperty("Id").GetGuid());
            Assert.DoesNotContain("stored content", store.Snapshot);
            Assert.DoesNotContain("stored formatting", store.Snapshot);
            Assert.False(detail.RemoveResourceFromTask(removed));
        });
    }

    [Theory]
    [InlineData(ResourceKind.File)]
    [InlineData(ResourceKind.Folder)]
    [InlineData(ResourceKind.Image)]
    [InlineData(ResourceKind.Audio)]
    public void RemovalLeavesExternalFilesAndFolderContentsUntouched(ResourceKind kind)
    {
        var directory = Directory.CreateTempSubdirectory("2do3-resource-removal-");
        var path = Path.Combine(directory.FullName, "original.dat");
        byte[] content = [0, 1, 2, 255];
        File.WriteAllBytes(path, content);
        try
        {
            var resource = new ResourceItem
            {
                Kind = kind, Content = kind == ResourceKind.Folder ? directory.FullName : path
            };
            var task = new TaskItem();
            task.Resources.Add(resource);
            var detail = CreateDetail(task);

            Assert.True(detail.RemoveResourceFromTask(resource));

            Assert.Empty(task.Resources);
            Assert.Null(detail.SelectedResource);
            Assert.True(Directory.Exists(directory.FullName));
            Assert.Equal(content, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
            directory.Delete();
        }
    }

    [Fact]
    public void SharedScopeOnlyAllowsRemovalOfCurrentTasksResources()
    {
        var task = new TaskItem { Id = 1, Tags = "Shared" };
        var otherTask = new TaskItem { Id = 2, Tags = "Shared" };
        var own = new ResourceItem { Kind = ResourceKind.Text, Content = "own text" };
        var other = new ResourceItem { Kind = ResourceKind.Text, Content = "other text" };
        var global = new ResourceItem { Kind = ResourceKind.Text, Content = "global text" };
        task.Resources.Add(own);
        otherTask.Resources.Add(other);
        var globals = new TagResourceCollection { Tag = "Shared" };
        globals.Resources.Add(global);
        var detail = CreateDetail(task, [task, otherTask], [globals]);
        detail.SelectedResourceScope = ResourceScopeOption.ForTagAll("Shared");
        detail.RefreshResourceGroups();

        Assert.False(detail.CanRemoveResourceFromTask(other));
        Assert.False(detail.RemoveResourceFromTask(other));
        Assert.True(detail.RemoveResourceFromTask(own));
        Assert.Same(other, Assert.Single(otherTask.Resources));

        detail.SelectedResourceScope = ResourceScopeOption.ForTagGlobal("Shared");
        detail.RefreshResourceGroups();
        Assert.False(detail.CanRemoveResourceFromTask(global));
        Assert.False(detail.RemoveResourceFromTask(global));
        Assert.Same(global, Assert.Single(globals.Resources));
        Assert.Equal("global text", global.Content);
    }

    private static TaskDetailViewModel CreateDetail(
        TaskItem task, IEnumerable<TaskItem>? tasks = null, IEnumerable<TagResourceCollection>? globals = null) =>
        new(task, tasks ?? [task], globals ?? [], (_, _) => null, new TagSettings(),
            new Surf2IntegrationSettings(), new Surf2IntegrationService(), new Surf2Launcher());

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class SnapshotTaskStore(TaskItem task) : ITaskStore
    {
        public string? Snapshot { get; set; }
        public bool IsConfigured => true;
        public IReadOnlyList<TaskItem> LoadTasks() => [task];
        public IReadOnlyList<TagResourceCollection> LoadTagResources() => [];
        public void SaveTask(TaskItem savedTask) => Snapshot = JsonSerializer.Serialize(savedTask);
        public void SaveTagResource(string tag, ResourceItem resource, int sortOrder) => throw new NotSupportedException();
        public void DeleteTask(int taskId) => throw new NotSupportedException();
    }

    private sealed class EmptyEmailProvider : IEmailProvider
    {
        public IReadOnlyList<EmailMessage> LoadCachedMessages() => [];
        public Task<EmailSyncResult> RefreshInboxAsync(
            EmailSettings settings, bool allowInteractiveSignIn, Window? owner, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
