using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
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

    private void ActionListOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button
            || DataContext is not TaskDetailViewModel viewModel)
        {
            return;
        }

        var menu = new ContextMenu();
        AddMenuItem(menu, "Copy as Text", () => CopyActionsAsText(viewModel));
        AddMenuItem(menu, "Copy as CSV", () => CopyActionsAsCsv(viewModel));
        AddMenuItem(menu, "Export to CSV", () => ExportActionsAsCsv(viewModel));

        menu.PlacementTarget = button;
        menu.IsOpen = true;
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
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != (ModifierKeys.Control | ModifierKeys.Alt))
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
                viewModel.UnindentAction(action);
                break;
            case Key.Right:
                viewModel.IndentAction(action);
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
            || sender is not ResourceLinkRichTextBox editor
            || editor.DataContext is not ActionItem action)
        {
            return;
        }

        if (e.Key == Key.Down && editor.IsCaretOnLastLine())
        {
            e.Handled = FocusRelativeActionTextBox(action, 1, moveToEnd: false);
        }
        else if (e.Key == Key.Up && editor.IsCaretOnFirstLine())
        {
            e.Handled = FocusRelativeActionTextBox(action, -1, moveToEnd: true);
        }
    }

    private static void CopyActionsAsText(TaskDetailViewModel viewModel)
    {
        SetClipboardText(CreateActionsText(viewModel.Actions));
    }

    private static void CopyActionsAsCsv(TaskDetailViewModel viewModel)
    {
        SetClipboardText(CreateActionsCsv(viewModel.Actions));
    }

    private static void ExportActionsAsCsv(TaskDetailViewModel viewModel)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export action list",
            FileName = $"{GetSafeFileName(viewModel.Task.Title)} actions.csv",
            Filter = "CSV files|*.csv|All files|*.*",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, CreateActionsCsv(viewModel.Actions), Encoding.UTF8);
    }

    private static string CreateActionsText(IEnumerable<ActionItem> actions)
    {
        return string.Join(
            Environment.NewLine,
            actions.Select(action =>
                $"{new string('\t', action.IndentLevel)}{action.ActionNumber} {action.ActionText} [{FormatStatus(action.Status)}]"));
    }

    private static string CreateActionsCsv(IEnumerable<ActionItem> actions)
    {
        var lines = new List<string>
        {
            "ActionNumber,ActionText,Status,IndentLevel"
        };

        lines.AddRange(actions.Select(action => string.Join(
            ",",
            EscapeCsv(action.ActionNumber),
            EscapeCsv(action.ActionText),
            EscapeCsv(FormatStatus(action.Status)),
            action.IndentLevel.ToString())));

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains('"') && !value.Contains(',') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Clipboard.Clear();
            return;
        }

        Clipboard.SetText(text);
    }

    private static string GetSafeFileName(string value)
    {
        var fileName = string.IsNullOrWhiteSpace(value) ? "Task" : value.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '-');
        }

        return fileName;
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

            var editor = FindVisualChild<ResourceLinkRichTextBox>(row);
            if (editor is null)
            {
                row.Focus();
                return;
            }

            editor.FocusText(moveToEnd);
        }, DispatcherPriority.Background);
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
