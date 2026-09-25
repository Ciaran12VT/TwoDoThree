using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using TwoDoThree.Services;

namespace TwoDoThree.Views;

public partial class MarkdownEditWindow : Window
{
    private readonly MarkdownTaskFile file;
    private readonly int maximumBytes;
    private bool isSaving;
    private bool allowClose;
    private bool initialized;

    public MarkdownEditWindow(MarkdownTaskFile file, string path, int maximumBytes)
    {
        this.file = file;
        this.maximumBytes = maximumBytes;
        InitializeComponent();
        PathBlock.Text = path;
        SourceEditor.Text = file.Text;
        initialized = true;
        SourceEditor_TextChanged(this, EventArgs.Empty);
        Loaded += (_, _) => SourceEditor.Focus();
        Closing += Window_Closing;
    }

    private bool HasChanges => SourceEditor.Text != file.Text;

    private void SourceEditor_TextChanged(object? sender, EventArgs e)
    {
        if (!initialized) return;
        Title = HasChanges ? "Edit Markdown — Unsaved changes" : "Edit Markdown";
        StatusBlock.Text = HasChanges ? "Unsaved changes. Save writes to the original file." : "Edit the Markdown source. Ctrl+S saves and returns to preview.";
    }

    private async Task<bool> SaveAsync()
    {
        if (isSaving) return false;
        if (!HasChanges) return true;
        var text = SourceEditor.Text;
        isSaving = true;
        SourceEditor.IsReadOnly = true;
        SaveButton.IsEnabled = CancelButton.IsEnabled = false;
        StatusBlock.Text = "Saving…";
        try
        {
            await Task.Run(() => file.SaveText(text, maximumBytes));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            StatusBlock.Text = $"Could not save: {ex.Message} Your edits are still available here.";
            return false;
        }
        finally
        {
            isSaving = false;
            SourceEditor.IsReadOnly = false;
            SaveButton.IsEnabled = CancelButton.IsEnabled = true;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveAsync()) { allowClose = true; DialogResult = true; }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (isSaving) { e.Cancel = true; return; }
        if (!HasChanges) return;
        e.Cancel = true;
        var choice = MessageBox.Show(this, "Save your changes to this Markdown file?\nYes: save. No: discard. Cancel: keep editing.",
            "Unsaved Markdown changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.No) FinishClosing(false);
        else if (choice == MessageBoxResult.Yes && await SaveAsync()) FinishClosing(true);
    }

    private void FinishClosing(bool saved) => Dispatcher.BeginInvoke(new Action(() =>
    {
        allowClose = true;
        if (saved) DialogResult = true;
        else Close();
    }));

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (!isSaving) SaveButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
}
