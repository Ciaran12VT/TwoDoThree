using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TwoDoThree.Controls;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Views;

public partial class TaskDetailWindow : Window
{
    private const double ToolbarScrollAmount = 42;

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
    private bool isUpdatingTaskStatusSelection;
    private GridLength expandedResourcePaneWidth = new(292);
    private GridLength expandedResourceSectionHeight = new(3, GridUnitType.Star);

    public TaskDetailWindow(TaskItem task, TagSettings tagSettings)
    {
        InitializeComponent();
        DataContext = new TaskDetailViewModel(task, tagSettings);
    }

    private void ResourceTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { DataContext: ResourceItem resource } item
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
        viewModel.SelectedResource = resource;
        e.Handled = true;
    }

    private void ResourceTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void ResourceTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { DataContext: ResourceItem resource })
        {
            return;
        }

        DragDrop.DoDragDrop(ResourceTree, ResourceLinkHelper.CreateDataObject(resource), DragDropEffects.Copy);
    }

    private void ResourceTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        RenameResourceMenuItem.IsEnabled = ResourceTree.SelectedItem is ResourceItem;
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

        if (!StatusMessageWindow.TryPrompt(this, status, out var statusMessage))
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
