using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void PeriodsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { Item: TaskStatusPeriod period }
            || DataContext is not TaskStatusPeriodsViewModel viewModel)
        {
            return;
        }

        var window = new TaskStatusPeriodEditWindow(period)
        {
            Owner = this
        };

        while (window.ShowDialog() == true)
        {
            if (viewModel.TryUpdatePeriod(
                    period,
                    window.StartTime,
                    window.EndTime,
                    window.EditedStatusMessage,
                    out var errorMessage))
            {
                break;
            }

            window = new TaskStatusPeriodEditWindow(period)
            {
                Owner = this,
                ErrorMessage = errorMessage,
                StartText = window.StartText,
                EndText = window.EndText,
                StatusMessage = window.StatusMessage
            };
        }

        e.Handled = true;
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
