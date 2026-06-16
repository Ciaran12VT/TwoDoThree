using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Controls;

public partial class ActionListControl : UserControl
{
    public ActionListControl()
    {
        InitializeComponent();
    }

    private void ActionShortcutsInfoButton_Click(object sender, RoutedEventArgs e)
    {
        ActionShortcutsPopup.IsOpen = !ActionShortcutsPopup.IsOpen;
    }

    private void ActionOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ActionItem action } button
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        ActionsGrid.SelectedItem = action;
        viewModel.SelectedAction = action;

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

    private void ActionsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (ActionsGrid.SelectedItem is not ActionItem action
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        var handled = true;
        var shouldFocusText = true;

        switch (e.Key)
        {
            case Key.Left:
                viewModel.IndentAction(action);
                break;
            case Key.Right:
                viewModel.UnindentAction(action);
                break;
            case Key.Up:
                viewModel.MoveActionUp(action);
                break;
            case Key.Down:
                viewModel.MoveActionDown(action);
                break;
            case Key.Delete:
                viewModel.DeleteAction(action);
                break;
            case Key.Enter:
                viewModel.AddActionBelow(action);
                break;
            case Key.OemPeriod:
                viewModel.CycleActionStatus(action, 1);
                shouldFocusText = false;
                break;
            case Key.OemComma:
                viewModel.CycleActionStatus(action, -1);
                shouldFocusText = false;
                break;
            default:
                handled = false;
                break;
        }

        if (!handled)
        {
            return;
        }

        e.Handled = true;
        if (shouldFocusText)
        {
            FocusSelectedActionTextBox(moveToEnd: false);
        }
    }

    private void ActionTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActionItem action }
            && DataContext is TaskDetailViewModel viewModel)
        {
            ActionsGrid.SelectedItem = action;
            viewModel.SelectedAction = action;
        }
    }

    private void ActionTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None
            || sender is not TextBox textBox
            || textBox.DataContext is not ActionItem action)
        {
            return;
        }

        if (e.Key == Key.Down && IsCaretOnLastLine(textBox))
        {
            e.Handled = FocusRelativeActionTextBox(action, 1, moveToEnd: false);
        }
        else if (e.Key == Key.Up && IsCaretOnFirstLine(textBox))
        {
            e.Handled = FocusRelativeActionTextBox(action, -1, moveToEnd: true);
        }
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

    private bool FocusRelativeActionTextBox(ActionItem action, int offset, bool moveToEnd)
    {
        var index = ActionsGrid.Items.IndexOf(action);
        var nextIndex = index + offset;
        if (nextIndex < 0 || nextIndex >= ActionsGrid.Items.Count)
        {
            return false;
        }

        ActionsGrid.SelectedIndex = nextIndex;
        FocusSelectedActionTextBox(moveToEnd);
        return true;
    }

    private void FocusSelectedActionTextBox(bool moveToEnd)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ActionsGrid.UpdateLayout();
            if (ActionsGrid.SelectedItem is null)
            {
                return;
            }

            ActionsGrid.ScrollIntoView(ActionsGrid.SelectedItem);
            ActionsGrid.UpdateLayout();

            if (ActionsGrid.ItemContainerGenerator.ContainerFromItem(ActionsGrid.SelectedItem) is not DataGridRow row)
            {
                return;
            }

            var textBox = FindVisualChild<TextBox>(row);
            if (textBox is null)
            {
                row.Focus();
                return;
            }

            textBox.Focus();
            textBox.CaretIndex = moveToEnd ? textBox.Text.Length : 0;
        }, DispatcherPriority.Background);
    }

    private static bool IsCaretOnFirstLine(TextBox textBox)
    {
        return textBox.GetLineIndexFromCharacterIndex(textBox.CaretIndex) == 0;
    }

    private static bool IsCaretOnLastLine(TextBox textBox)
    {
        var lineIndex = textBox.GetLineIndexFromCharacterIndex(textBox.CaretIndex);
        return lineIndex >= Math.Max(0, textBox.LineCount - 1);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
