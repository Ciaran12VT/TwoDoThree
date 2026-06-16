using System.Windows;
using System.Windows.Controls;
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
