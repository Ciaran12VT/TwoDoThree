using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;
using TwoDoThree.Views;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(ViewModel.Settings)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            ViewModel.RefreshEmails();
        }
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        OpenTaskDetail(ViewModel.CreateEmptyTask(), activate: true);
    }

    private void CreateTaskFromEmailMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEmail is EmailMessage email)
        {
            OpenTaskDetail(ViewModel.CreateTaskFromEmail(email), activate: true);
        }
    }

    private void EmailListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private void TasksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TasksGrid.SelectedItem is TaskItem task)
        {
            OpenTaskDetail(task, activate: true);
        }
    }

    private void TasksGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { } row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void PeekTaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is { } task)
        {
            OpenTaskDetail(task, activate: false);
        }
    }

    private void SetTaskStatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: TaskItemStatus status }
            && ViewModel.SelectedTask is { } task)
        {
            task.Status = status;
            ViewModel.TasksView.Refresh();
        }
    }

    private void OpenTaskDetail(TaskItem task, bool activate)
    {
        if (activate)
        {
            task.Status = TaskItemStatus.Active;
        }

        var window = new TaskDetailWindow(task)
        {
            Owner = this
        };

        window.ShowDialog();
        ViewModel.TasksView.Refresh();
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
