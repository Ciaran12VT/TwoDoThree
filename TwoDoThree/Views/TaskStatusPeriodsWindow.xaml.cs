using System.Windows;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Views;

public partial class TaskStatusPeriodsWindow : Window
{
    public TaskStatusPeriodsWindow(TaskItem task)
    {
        InitializeComponent();
        DataContext = new TaskStatusPeriodsViewModel(task);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
