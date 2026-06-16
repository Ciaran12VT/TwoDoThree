using System.Windows;
using System.Windows.Controls;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Views;

public partial class TaskDetailWindow : Window
{
    private bool isResourceTreeVisible = true;

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

        ResourceTreeColumn.Width = isResourceTreeVisible
            ? new GridLength(260)
            : new GridLength(0);
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
