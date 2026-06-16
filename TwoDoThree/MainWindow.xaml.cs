using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;
using TwoDoThree.Views;

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
        OpenTaskDetail(ViewModel.CreateEmptyTask());
    }

    private void CreateTaskFromEmailMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EmailMessage email })
        {
            OpenTaskDetail(ViewModel.CreateTaskFromEmail(email));
        }
    }

    private void TasksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TasksGrid.SelectedItem is TaskItem task)
        {
            OpenTaskDetail(task);
        }
    }

    private void OpenTaskDetail(TaskItem task)
    {
        var window = new TaskDetailWindow(task)
        {
            Owner = this
        };

        window.ShowDialog();
        ViewModel.TasksView.Refresh();
    }
}
