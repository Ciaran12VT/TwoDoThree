using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TwoDoThree.Controls;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TwoDoThree.ViewModels;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Views;

public partial class TaskDetailWindow : Window
{
    private const double ToolbarScrollAmount = 42;
    private const int FolderPageSize = 250;
    private const string MakeGlobalMenuItemTagPrefix = "MakeGlobal:";

    private static readonly string[] TextFontFamilies =
    [
        "Segoe UI",
        "Calibri",
        "Arial",
        "Times New Roman",
        "Georgia",
        "Verdana",
        "Consolas",
        "Courier New"
    ];

    private static readonly double[] TextFontSizes = [10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48];

    private bool isResourceTreeVisible = true;
    private Point resourceDragStart;
    private ResourceItem? resourceDragSource;
    private bool isUpdatingTaskStatusSelection;
    private GridLength expandedResourcePaneWidth = new(292);
    private GridLength expandedResourceSectionHeight = new(3, GridUnitType.Star);

    public TaskDetailWindow(
        TaskItem task,
        IEnumerable<TaskItem> tasks,
        IEnumerable<TagResourceCollection> globalTagResources,
        Func<string, ResourceItem, ResourceItem?> makeResourceGlobal,
        TagSettings tagSettings,
        Surf2IntegrationSettings surf2Settings,
        ISurf2IntegrationService surf2IntegrationService,
        ISurf2Launcher surf2Launcher)
    {
        InitializeComponent();
        DataContext = new TaskDetailViewModel(
            task,
            tasks,
            globalTagResources,
            makeResourceGlobal,
            tagSettings,
            surf2Settings,
            surf2IntegrationService,
            surf2Launcher);
        Loaded += TaskDetailWindow_Loaded;
        ResourceTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(ResourceTreeItem_Expanded));
        ResourceTree.AddHandler(TreeViewItem.SelectedEvent, new RoutedEventHandler(ResourceTreeItem_Selected));
    }

    public bool WasClosedWithCloseButton { get; private set; }

    private async void TaskDetailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TaskDetailViewModel viewModel)
        {
            await viewModel.InitializeSurf2Async();
        }
    }

    private void ResourceTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }
        if (item.DataContext is not ResourceItem and not FileSystemResourceNode { Message: null }) return;

        item.IsSelected = true;
        item.Focus();
        if (item.DataContext is FileSystemResourceNode { Message: null } node)
        {
            OpenLinkedPath(node.Path);
        }
        else if (item.DataContext is ResourceItem resource)
        {
            viewModel.SelectedResource = resource;
            if (resource.Kind == ResourceKind.SurfResource) viewModel.OpenLinkedResource(resource);
            else if (resource.Kind is ResourceKind.File or ResourceKind.Folder) OpenLinkedPath(resource.Content);
        }

        e.Handled = true;
    }

    private async void ResourceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not TaskDetailViewModel viewModel) return;

        switch (e.NewValue)
        {
            case ResourceItem resource:
                viewModel.SelectedResource = resource;
                if (resource.Kind == ResourceKind.VirtualFolder) resource.IsExpanded = true;
                if (resource.Kind == ResourceKind.Folder)
                    await LoadRootFolderAsync(resource);
                break;
            case FileSystemResourceNode { NextOffset: int offset, Siblings: { } siblings } more:
                await LoadFolderPageAsync(more.Path, siblings, offset);
                break;
            case FileSystemResourceNode { Message: null } node:
                viewModel.SelectedResource = new ResourceItem
                {
                    Name = node.Name,
                    Kind = node.IsFolder ? ResourceKind.Folder : ResourceKind.File,
                    Content = node.Path
                };
                if (node.IsFolder) await LoadChildFolderAsync(node);
                break;
        }
    }

    private async void ResourceTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        switch ((e.OriginalSource as TreeViewItem)?.DataContext)
        {
            case ResourceItem { Kind: ResourceKind.Folder } resource:
                await LoadRootFolderAsync(resource);
                break;
            case FileSystemResourceNode { IsFolder: true } node:
                await LoadChildFolderAsync(node);
                break;
        }
    }

    private void ResourceTreeItem_Selected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item &&
            (item.DataContext is ResourceItem { Kind: ResourceKind.Folder }
             || item.DataContext is FileSystemResourceNode { IsFolder: true }))
        {
            item.IsExpanded = true;
        }
    }

    private async Task LoadRootFolderAsync(ResourceItem resource)
    {
        if (resource.FolderChildrenLoaded) return;
        resource.FolderChildrenLoaded = true;
        await LoadFolderPageAsync(resource.Content, resource.FolderChildren, 0);
    }

    private async Task LoadChildFolderAsync(FileSystemResourceNode node)
    {
        if (node.IsLoaded) return;
        node.IsLoaded = true;
        await LoadFolderPageAsync(node.Path, node.Children, 0);
    }

    private static async Task LoadFolderPageAsync(string path, ObservableCollection<FileSystemResourceNode> children, int offset)
    {
        if (offset == 0) children.Clear();
        else
        {
            var more = children.FirstOrDefault(child => child.NextOffset == offset);
            if (more is not null) children.Remove(more);
        }

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            children.Add(new FileSystemResourceNode(path, false, "Folder is missing or inaccessible"));
            return;
        }

        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                children.Add(new FileSystemResourceNode(path, false, "Linked folder: open in Explorer to browse"));
                return;
            }

            var page = await Task.Run(() => Directory.EnumerateFileSystemEntries(path)
                .Skip(offset)
                .Take(FolderPageSize + 1)
                .Select(entry => new FileSystemResourceNode(entry, Directory.Exists(entry)))
                .ToList());

            foreach (var child in page.Take(FolderPageSize)) children.Add(child);
            if (page.Count > FolderPageSize)
            {
                var more = new FileSystemResourceNode(path, false, "Load more…", offset + FolderPageSize)
                {
                    Siblings = children
                };
                children.Add(more);
            }
            else if (offset == 0 && page.Count == 0)
            {
                children.Add(new FileSystemResourceNode(path, false, "Folder is empty"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            children.Add(new FileSystemResourceNode(path, false, $"Cannot list folder: {ex.Message}"));
        }
    }

    private void OpenLinkedPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                MessageBox.Show(this, "The linked file or folder is missing or inaccessible.", "Open resource", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Open resource", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResourceTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void ResourceTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        resourceDragStart = e.GetPosition(ResourceTree);
        resourceDragSource = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as ResourceItem;
    }

    private void ResourceTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || resourceDragSource is not { } resource)
        {
            return;
        }

        var position = e.GetPosition(ResourceTree);
        if (Math.Abs(position.X - resourceDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - resourceDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        resourceDragSource = null;
        DragDrop.DoDragDrop(ResourceTree, ResourceLinkHelper.CreateDataObject(resource), DragDropEffects.Copy | DragDropEffects.Move);
    }

    private ResourceItem? GetVirtualFolderDropTarget(DependencyObject? source)
    {
        var item = FindAncestor<TreeViewItem>(source);
        while (item is not null)
        {
            if (item.DataContext is ResourceItem { Kind: ResourceKind.VirtualFolder } folder)
                return DataContext is TaskDetailViewModel model && model.Task.Resources.Contains(folder) ? folder : null;
            item = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(item));
        }
        return null;
    }

    private void Resources_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not TaskDetailViewModel model) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        else if (FindAncestor<TreeView>(e.OriginalSource as DependencyObject) == ResourceTree
                 && ResourceLinkHelper.TryGetDraggedResource(e, out var resource) && resource is not null)
        {
            e.Effects = model.CanMoveResource(resource, GetVirtualFolderDropTarget(e.OriginalSource as DependencyObject))
                && (e.AllowedEffects & DragDropEffects.Move) != 0 ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Resources_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not TaskDetailViewModel model) return;
        var folder = GetVirtualFolderDropTarget(e.OriginalSource as DependencyObject);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            if ((e.AllowedEffects & DragDropEffects.Copy) != 0
                && e.Data.GetData(DataFormats.FileDrop) is string[] paths
                && model.AddLocalPaths(paths, folder).Count > 0)
                e.Effects = DragDropEffects.Copy;
        }
        else if (FindAncestor<TreeView>(e.OriginalSource as DependencyObject) == ResourceTree
                 && ResourceLinkHelper.TryGetDraggedResource(e, out var resource) && resource is not null)
        {
            e.Handled = true;
            e.Effects = (e.AllowedEffects & DragDropEffects.Move) != 0 && model.MoveResource(resource, folder)
                ? DragDropEffects.Move : DragDropEffects.None;
        }
    }

    private void ResourceTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        RemoveMakeGlobalResourceMenuItems();

        RenameResourceMenuItem.IsEnabled = ResourceTree.SelectedItem is ResourceItem;
        // A background right-click must not remove the previously selected resource.
        var isResourceTarget = e.CursorLeft < 0
            || FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext is ResourceItem;
        PopulateVirtualFolderMenu(isResourceTarget);
        RemoveResourceMenuItem.IsEnabled = isResourceTarget
            && ResourceTree.SelectedItem is ResourceItem selected
            && DataContext is TaskDetailViewModel model && model.CanRemoveResourceFromTask(selected);
        if (ResourceTree.SelectedItem is not ResourceItem { Kind: not ResourceKind.VirtualFolder }
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        var tags = viewModel.ResourceScopeTags;
        if (tags.Count == 0 || ResourceTree.ContextMenu is null)
        {
            return;
        }

        ResourceTree.ContextMenu.Items.Add(new Separator
        {
            Tag = MakeGlobalMenuItemTagPrefix
        });

        foreach (var tag in tags)
        {
            var item = new MenuItem
            {
                Header = $"Make Global for {tag}",
                Tag = $"{MakeGlobalMenuItemTagPrefix}{tag}"
            };
            item.Click += MakeGlobalResourceMenuItem_Click;
            ResourceTree.ContextMenu.Items.Add(item);
        }
    }

    private void NewVirtualFolderMenuItem_Click(object sender, RoutedEventArgs e) => CreateVirtualFolder(null);

    private void NewVirtualFolderInsideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceTree.SelectedItem is ResourceItem { Kind: ResourceKind.VirtualFolder } folder)
            CreateVirtualFolder(folder);
    }

    private void CreateVirtualFolder(ResourceItem? parent)
    {
        if (DataContext is not TaskDetailViewModel model) return;
        var dialog = new RenameResourceWindow("Virtual folder", creatingVirtualFolder: true) { Owner = this };
        if (dialog.ShowDialog() == true) model.CreateVirtualFolder(dialog.ResourceName, parent);
    }

    private void PopulateVirtualFolderMenu(bool isResourceTarget)
    {
        MoveToVirtualFolderMenuItem.Items.Clear();
        var model = DataContext as TaskDetailViewModel;
        var resource = isResourceTarget ? ResourceTree.SelectedItem as ResourceItem : null;
        NewVirtualFolderInsideMenuItem.IsEnabled = resource?.Kind == ResourceKind.VirtualFolder
            && model?.Task.Resources.Contains(resource) == true;
        MoveToVirtualFolderMenuItem.IsEnabled = resource is not null && model?.CanMoveResource(resource, null) == true;
        if (!MoveToVirtualFolderMenuItem.IsEnabled || resource is null || model is null) return;

        var rootItem = new MenuItem { Header = "Files and Folders (root)" };
        rootItem.Click += (_, _) => model.MoveResource(resource, null);
        MoveToVirtualFolderMenuItem.Items.Add(rootItem);
        foreach (var folder in model.Task.Resources.Where(item => item.Kind == ResourceKind.VirtualFolder
                     && model.CanMoveResource(resource, item)))
        {
            var item = new MenuItem { Header = folder.Name };
            item.Click += (_, _) => model.MoveResource(resource, folder);
            MoveToVirtualFolderMenuItem.Items.Add(item);
        }
    }

    private void RenameResourceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceTree.SelectedItem is not ResourceItem resource)
        {
            return;
        }

        var window = new RenameResourceWindow(resource.Name)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            resource.Name = window.ResourceName;
            if (DataContext is TaskDetailViewModel viewModel)
            {
                viewModel.RefreshResourceGroups(resource);
            }
        }
    }

    private void RemoveResourceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceTree.SelectedItem is not ResourceItem resource
            || DataContext is not TaskDetailViewModel viewModel
            || !viewModel.RemoveResourceFromTask(resource)) return;

        // Do not leave an editor open for a resource that is no longer stored on this task.
        foreach (var window in OwnedWindows.OfType<ResourcePopoutWindow>()
                     .Where(window => ReferenceEquals(window.Resource, resource)).ToList())
        {
            window.Close();
        }
    }

    private void MakeGlobalResourceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tagToken }
            || !tagToken.StartsWith(MakeGlobalMenuItemTagPrefix, StringComparison.Ordinal)
            || ResourceTree.SelectedItem is not ResourceItem resource
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        var tag = tagToken[MakeGlobalMenuItemTagPrefix.Length..];
        viewModel.MakeResourceGlobalForTag(resource, tag);
    }

    private void RemoveMakeGlobalResourceMenuItems()
    {
        if (ResourceTree.ContextMenu is null)
        {
            return;
        }

        for (var index = ResourceTree.ContextMenu.Items.Count - 1; index >= 0; index--)
        {
            if (ResourceTree.ContextMenu.Items[index] is FrameworkElement { Tag: string tag }
                && tag.StartsWith(MakeGlobalMenuItemTagPrefix, StringComparison.Ordinal))
            {
                ResourceTree.ContextMenu.Items.RemoveAt(index);
            }
        }
    }

    private void TagsEditor_ManageTagsRequested(object? sender, EventArgs e)
    {
        if (DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        var window = new TagManagerWindow(viewModel.TagSettings, TagsEditor.CurrentToken)
        {
            Owner = this
        };

        var requestedTag = TagsEditor.CurrentToken;
        if (window.ShowDialog() == true
            && !string.IsNullOrWhiteSpace(requestedTag)
            && viewModel.AvailableTags.FirstOrDefault(tag =>
                string.Equals(tag, requestedTag, StringComparison.OrdinalIgnoreCase)) is { } addedTag)
        {
            TagsEditor.ApplyTag(addedTag);
        }
    }

    private void TaskStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingTaskStatusSelection
            || DataContext is not TaskDetailViewModel viewModel
            || sender is not ComboBox comboBox
            || comboBox.SelectedItem is not TaskItemStatus status
            || status == viewModel.Task.Status)
        {
            return;
        }

        if (!StatusMessageWindow.TryPrompt(this, viewModel.Task, status, out var statusMessage))
        {
            isUpdatingTaskStatusSelection = true;
            try
            {
                comboBox.SelectedItem = viewModel.Task.Status;
            }
            finally
            {
                isUpdatingTaskStatusSelection = false;
            }

            return;
        }

        viewModel.SetTaskStatus(status, statusMessage);
    }

    private void FontFamilyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var fontFamily in TextFontFamilies)
        {
            var item = new MenuItem
            {
                Header = fontFamily,
                FontFamily = new FontFamily(fontFamily)
            };
            item.Click += (_, _) => ApplyTextFormatting(editor => editor.SetSelectionFontFamily(fontFamily));
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void FontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var fontSize in TextFontSizes)
        {
            var item = new MenuItem
            {
                Header = fontSize.ToString("0")
            };
            item.Click += (_, _) => ApplyTextFormatting(editor => editor.SetSelectionFontSize(fontSize));
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTextFormatting(editor => editor.ToggleBold());
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTextFormatting(editor => editor.ToggleItalic());
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTextFormatting(editor => editor.ToggleUnderline());
    }

    private void StrikethroughButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTextFormatting(editor => editor.ToggleStrikethrough());
    }

    private void PopOutResourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TaskDetailViewModel viewModel
            || viewModel.SelectedResource is not { } resource)
        {
            return;
        }

        var window = new ResourcePopoutWindow(viewModel, resource)
        {
            Owner = this
        };

        window.Show();
    }

    private void ApplyTextFormatting(Action<ResourceLinkRichTextBox> action)
    {
        if (DataContext is not TaskDetailViewModel { SelectedResource.Kind: ResourceKind.Text }
            || SelectedResourceViewer.GetTextEditor() is not { } editor)
        {
            return;
        }

        action(editor);
    }

    private void InternalToolbarScrollUpButton_Click(object sender, RoutedEventArgs e)
    {
        ScrollToolbar(InternalResourceToolbarScrollViewer, -ToolbarScrollAmount);
    }

    private void InternalToolbarScrollDownButton_Click(object sender, RoutedEventArgs e)
    {
        ScrollToolbar(InternalResourceToolbarScrollViewer, ToolbarScrollAmount);
    }

    private void ResourceAddToolbarScrollUpButton_Click(object sender, RoutedEventArgs e)
    {
        ScrollToolbar(ResourceAddToolbarScrollViewer, -ToolbarScrollAmount);
    }

    private void ResourceAddToolbarScrollDownButton_Click(object sender, RoutedEventArgs e)
    {
        ScrollToolbar(ResourceAddToolbarScrollViewer, ToolbarScrollAmount);
    }

    private static void ScrollToolbar(ScrollViewer scrollViewer, double delta)
    {
        scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + delta));
    }

    private void ToggleResourceTreeButton_Click(object sender, RoutedEventArgs e)
    {
        isResourceTreeVisible = !isResourceTreeVisible;

        if (!isResourceTreeVisible)
        {
            expandedResourcePaneWidth = ResourcePaneColumn.ActualWidth > 40
                ? new GridLength(ResourcePaneColumn.ActualWidth)
                : new GridLength(292);
        }

        ResourcePaneColumn.Width = isResourceTreeVisible
            ? expandedResourcePaneWidth
            : new GridLength(32);
        ResourcePaneColumn.MinWidth = isResourceTreeVisible ? 180 : 32;
        ResourceTreeColumn.Width = isResourceTreeVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ResourceTreePanel.Visibility = isResourceTreeVisible ? Visibility.Visible : Visibility.Collapsed;
        ResourceGridSplitter.Visibility = isResourceTreeVisible ? Visibility.Visible : Visibility.Collapsed;
        ResourceSplitterColumn.Width = isResourceTreeVisible ? GridLength.Auto : new GridLength(0);
        ToggleResourceTreeButton.Content = isResourceTreeVisible ? ">" : "<";
    }

    private void ResourcesExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (ResourceSectionRow is null
            || ResourceActionSplitterRow is null
            || ResourceActionGridSplitter is null)
        {
            return;
        }

        ResourceSectionRow.Height = expandedResourceSectionHeight;
        ResourceSectionRow.MinHeight = 180;
        ResourceActionSplitterRow.Height = GridLength.Auto;
        ResourceActionGridSplitter.Visibility = Visibility.Visible;
    }

    private void ResourcesExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        if (ResourceSectionRow is null
            || ResourceActionSplitterRow is null
            || ResourceActionGridSplitter is null)
        {
            return;
        }

        expandedResourceSectionHeight = ResourceSectionRow.Height;
        ResourceSectionRow.MinHeight = 0;
        ResourceSectionRow.Height = GridLength.Auto;
        ResourceActionSplitterRow.Height = new GridLength(0);
        ResourceActionGridSplitter.Visibility = Visibility.Collapsed;
    }

    private void TimeSpentTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        var window = new TaskStatusPeriodsWindow(viewModel.Task)
        {
            Owner = this
        };

        window.Show();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        WasClosedWithCloseButton = true;
        Close();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
