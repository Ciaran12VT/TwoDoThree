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
    private const string EmailDataFormat = "TwoDoThree.EmailMessage";

    private readonly List<TaskDetailWindow> openTaskDetailWindows = new();

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
        OpenTaskDetail(ViewModel.CreateEmptyTask(), activate: false);
    }

    private void CreateTaskFromEmailMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEmail is EmailMessage email)
        {
            OpenTaskDetail(ViewModel.CreateTaskFromEmail(email), activate: false);
        }
    }

    private void EmailListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: EmailMessage email } item)
        {
            return;
        }

        item.IsSelected = true;
        var data = new DataObject();
        data.SetData(EmailDataFormat, email);
        data.SetText(email.Subject);
        DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
    }

    private void EmailListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private void AssociatedTaskLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EmailMessage email })
        {
            return;
        }

        ViewModel.SelectedEmail = email;
        ViewModel.FilterTasksByEmail(email);
        e.Handled = true;
    }

    private void ClearTaskEmailFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearTaskEmailFilter();
    }

    private void TasksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TasksGrid.SelectedItem is TaskItem task)
        {
            OpenTaskDetail(task, activate: !HasOpenTaskDetailWindows);
        }
    }

    private void TasksGrid_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDraggedEmail(e, out _)
                    && FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { DataContext: TaskItem }
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TasksGrid_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedEmail(e, out var email)
            || email is null
            || FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { DataContext: TaskItem task } row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
        var resource = ViewModel.AddEmailResource(task, email);
        RefreshOpenTaskDetailWindows(task, resource);
        e.Handled = true;
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
            ViewModel.SetTaskStatus(task, status);
        }
    }

    private void OpenTaskDetail(TaskItem task, bool activate)
    {
        if (activate)
        {
            ViewModel.ActivateTask(task);
        }

        var window = new TaskDetailWindow(task)
        {
            Owner = this
        };

        openTaskDetailWindows.Add(window);
        window.Closed += (_, _) =>
        {
            openTaskDetailWindows.Remove(window);
            ViewModel.TasksView.Refresh();
        };

        window.Show();
    }

    private bool HasOpenTaskDetailWindows
    {
        get
        {
            openTaskDetailWindows.RemoveAll(window => !window.IsVisible);
            return openTaskDetailWindows.Count > 0;
        }
    }

    private void RefreshOpenTaskDetailWindows(TaskItem task, ResourceItem selectedResource)
    {
        openTaskDetailWindows.RemoveAll(window => !window.IsVisible);

        foreach (var window in openTaskDetailWindows)
        {
            if (window.DataContext is TaskDetailViewModel viewModel
                && ReferenceEquals(viewModel.Task, task))
            {
                viewModel.RefreshResourceGroups(selectedResource);
            }
        }
    }

    private static bool TryGetDraggedEmail(DragEventArgs e, out EmailMessage? email)
    {
        email = e.Data.GetData(EmailDataFormat) as EmailMessage;
        return email is not null;
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
