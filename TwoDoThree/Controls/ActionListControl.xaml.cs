using System.Windows;
using System.Windows.Controls;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Controls;

public partial class ActionListControl : UserControl
{
    public ActionListControl()
    {
        InitializeComponent();
    }

    private void ActionOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ActionItem action } button
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        var menu = new ContextMenu();
        AddMenuItem(menu, "Indent", () => viewModel.IndentAction(action));
        AddMenuItem(menu, "Unindent", () => viewModel.UnindentAction(action));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Move up", () => viewModel.MoveActionUp(action));
        AddMenuItem(menu, "Move down", () => viewModel.MoveActionDown(action));
        menu.Items.Add(new Separator());

        foreach (var status in Enum.GetValues<ActionStatus>())
        {
            AddMenuItem(menu, $"Set status: {FormatStatus(status)}", () => viewModel.SetActionStatus(action, status));
        }

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private static void AddMenuItem(ItemsControl menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static string FormatStatus(ActionStatus status)
    {
        return status switch
        {
            ActionStatus.NotStarted => "Not started",
            ActionStatus.InProgress => "In progress",
            _ => status.ToString()
        };
    }
}
