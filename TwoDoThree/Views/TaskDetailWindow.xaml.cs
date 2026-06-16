using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Views;

public partial class TaskDetailWindow : Window
{
    private bool isResourceTreeVisible = true;
    private GridLength expandedResourcePaneWidth = new(292);

    public TaskDetailWindow(TaskItem task)
    {
        InitializeComponent();
        DataContext = new TaskDetailViewModel(task);
    }

    private void ResourceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ResourceItem resource && DataContext is TaskDetailViewModel viewModel)
        {
            viewModel.SelectedResource = resource;
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
        }
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
