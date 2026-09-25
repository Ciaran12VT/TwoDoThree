using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using TwoDoThree.Services;

namespace TwoDoThree.Controls;

public partial class FileResourcePreviewControl
{
    private bool markdownModeChanging;
    private bool markdownSourceInitialized;
    private readonly List<Window> markdownOwners = [];
    private bool HasMarkdownDraft => markdownSourceInitialized && markdownTaskFile is not null
        && MarkdownSourceEditor.Text != markdownTaskFile.Text;

    private void PreviewLoaded(object sender, RoutedEventArgs e)
    {
        DetachMarkdownClosingHandlers();
        // Closing an owner can close its owned windows without raising their Closing
        // event, so protect the draft when either this popout or its owner is closed.
        for (var window = Window.GetWindow(this); window is not null; window = window.Owner)
        {
            markdownOwners.Add(window);
            window.Closing += MarkdownOwner_Closing;
        }
        _ = LoadPreviewAsync();
    }

    private void PreviewUnloaded(object sender, RoutedEventArgs e)
    {
        DetachMarkdownClosingHandlers();
        DetachMarkdownSession();
    }

    private void DetachMarkdownClosingHandlers()
    {
        foreach (var window in markdownOwners) window.Closing -= MarkdownOwner_Closing;
        markdownOwners.Clear();
    }

    private static object? CoerceResource(DependencyObject owner, object? value)
    {
        var preview = (FileResourcePreviewControl)owner;
        if (preview.markdownModeChanging) return preview.Resource;
        return ReferenceEquals(value, preview.Resource) || preview.ConfirmPendingMarkdownEdits()
            ? value : preview.Resource;
    }

    private void MarkdownOwner_Closing(object? sender, CancelEventArgs e)
    {
        if (markdownModeChanging || !ConfirmPendingMarkdownEdits()) e.Cancel = true;
    }

    internal bool ConfirmPendingMarkdownEdits()
    {
        if (markdownModeChanging) return false;
        if (!HasMarkdownDraft) return true;
        var choice = MessageBox.Show(Window.GetWindow(this),
            "Save your Markdown changes?\nYes: save. No: discard. Cancel: keep editing.",
            "Unsaved Markdown changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes) return SaveMarkdownDraft();
        if (choice != MessageBoxResult.No) return false;
        InitializeMarkdownSource(markdownTaskFile);
        return true;
    }

    private void InitializeMarkdownSource(MarkdownTaskFile? file)
    {
        draftPins = markdownSession?.Pins ?? [];
        markdownSourceInitialized = false;
        MarkdownSourceEditor.Text = UseNativeMarkdownRenderer ? file?.Text ?? string.Empty : string.Empty;
        markdownSourceInitialized = UseNativeMarkdownRenderer && file is not null;
        UpdateMarkdownEditStatus();
    }

    private void ResetMarkdownEditing()
    {
        DetachMarkdownSession();
        pinSidebarOpen = false;
        pinSidebarScroll = 0;
        markdownEditorOpen = false;
        markdownSourceInitialized = false;
        MarkdownSourceEditor.Visibility = Visibility.Collapsed;
        EditMarkdownButton.Content = "Edit source";
    }

    private void MarkdownSourceEditor_TextChanged(object? sender, EventArgs e)
    {
        if (markdownSourceInitialized) UpdateMarkdownEditStatus();
    }

    private void UpdateMarkdownEditStatus()
    {
        SaveMarkdownButton.IsEnabled = DiscardMarkdownButton.IsEnabled = HasMarkdownDraft && !markdownModeChanging;
        if (!markdownSourceInitialized) { MarkdownEditStatus.Text = string.Empty; return; }
        MarkdownEditStatus.Text = HasMarkdownDraft
            ? markdownEditorOpen ? "Unsaved changes. Save writes to the original file."
                : "Preview of unsaved changes. Save to write the file and enable task checkboxes."
            : markdownEditorOpen ? "Editing source. Ctrl+S saves. Preview keeps your place in the document." : string.Empty;
    }

    private void SetMarkdownBusy(bool busy)
    {
        markdownModeChanging = busy;
        EditMarkdownButton.IsEnabled = !busy && markdownTaskFile is not null;
        MarkdownSourceEditor.IsReadOnly = busy;
        SaveMarkdownButton.IsEnabled = DiscardMarkdownButton.IsEnabled = !busy && HasMarkdownDraft;
        if (!busy && markdownSession is not null) MarkdownSession_Changed(markdownSession, new(markdownSession.Text));
    }

    private async Task<MarkdownViewportAnchor> CaptureMarkdownAnchorAsync()
    {
        if (markdownEditorOpen) return CaptureSourceAnchor();
        await CapturePinViewAsync();
        try
        {
            return JsonSerializer.Deserialize<MarkdownViewportAnchor>(
                await MarkdownBrowser.ExecuteScriptAsync("captureMarkdownAnchor()")) ?? new(1, 0, "start");
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or System.Runtime.InteropServices.COMException)
        {
            return new(1, 0, "start");
        }
    }

    private MarkdownViewportAnchor CaptureSourceAnchor()
    {
        var view = MarkdownSourceEditor.TextArea.TextView;
        view.EnsureVisualLines();
        var top = MarkdownSourceEditor.VerticalOffset;
        if (top <= 1) return new(1, 0, "start");
        var y = top + view.ActualHeight * .2;
        var line = view.GetDocumentLineByVisualTop(y);
        var lineTop = view.GetVisualTopByDocumentLine(line.LineNumber);
        var fraction = Math.Clamp((y - lineTop) / view.DefaultLineHeight, 0, .999999);
        var atEnd = top + view.ActualHeight >= view.DocumentHeight - 1;
        return new(line.LineNumber + fraction, .2, atEnd ? "end" : null);
    }

    private void RestoreSourceAnchor(MarkdownViewportAnchor anchor)
    {
        MarkdownSourceEditor.UpdateLayout();
        var view = MarkdownSourceEditor.TextArea.TextView;
        if (anchor.edge == "start") { MarkdownSourceEditor.ScrollToVerticalOffset(0); return; }
        if (anchor.edge == "end") { MarkdownSourceEditor.ScrollToEnd(); return; }
        var line = Math.Clamp((int)anchor.line, 1, MarkdownSourceEditor.Document.LineCount);
        var top = view.GetVisualTopByDocumentLine(line) + (anchor.line - Math.Floor(anchor.line)) * view.DefaultLineHeight;
        MarkdownSourceEditor.ScrollToVerticalOffset(Math.Max(0, top - anchor.viewport * view.ActualHeight));
    }

    private async void EditMarkdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (!UseNativeMarkdownRenderer || markdownTaskFile is null || markdownModeChanging) return;
        SetMarkdownBusy(true);
        try
        {
            if (markdownTaskSave is not null)
            {
                try { await markdownTaskSave; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
            }
            var anchor = await CaptureMarkdownAnchorAsync();
            if (!markdownEditorOpen)
            {
                MarkdownSurface.Visibility = PreviewTextBox.Visibility = Visibility.Collapsed;
                markdownEditorOpen = true;
                MarkdownSourceEditor.Visibility = Visibility.Visible;
                EditMarkdownButton.Content = "Preview";
                MarkdownSourceEditor.CaretOffset = MarkdownSourceEditor.Document.GetLineByNumber(
                    Math.Clamp((int)anchor.line, 1, MarkdownSourceEditor.Document.LineCount)).Offset;
                MarkdownSourceEditor.Focus();
                // AvalonEdit brings its caret into view when focus/layout completes.
                // Restore the passage after that work so it cannot reset our viewport.
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                RestoreSourceAnchor(anchor);
            }
            else
            {
                markdownEditorOpen = false;
                MarkdownSourceEditor.Visibility = Visibility.Collapsed;
                EditMarkdownButton.Content = "Edit source";
                PreviewTextBox.Text = MarkdownSourceEditor.Text;
                await ShowMarkdownPreviewAsync(MarkdownSourceEditor.Text, ++previewVersion, !HasMarkdownDraft, anchor);
            }
        }
        finally
        {
            SetMarkdownBusy(false);
            UpdateMarkdownEditStatus();
        }
    }

    private bool SaveMarkdownDraft()
    {
        if (!HasMarkdownDraft) return true;
        try
        {
            applyingSessionChange = true;
            if (markdownSession is not null)
            {
                markdownSession.SaveText(markdownTaskFile!.Text, MarkdownSourceEditor.Text, draftPins);
                markdownTaskFile = markdownSession.LoadSnapshot();
                draftPins = markdownSession.Pins;
            }
            else markdownTaskFile!.SaveText(MarkdownSourceEditor.Text, MaxPreviewBytes);
            UpdateMarkdownEditStatus();
            MarkdownEditStatus.Text = markdownSession?.Error ?? "Saved to the original file.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            MarkdownEditStatus.Text = $"Could not save: {ex.Message} Your draft is still available here.";
            return false;
        }
        finally { applyingSessionChange = false; }
    }

    private async void SaveMarkdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (markdownModeChanging) return;
        SetMarkdownBusy(true);
        try
        {
            var anchor = await CaptureMarkdownAnchorAsync();
            if (SaveMarkdownDraft() && !markdownEditorOpen && markdownSourceInitialized)
                await ShowMarkdownPreviewAsync(MarkdownSourceEditor.Text, ++previewVersion, true, anchor);
        }
        finally { SetMarkdownBusy(false); }
    }

    private async void DiscardMarkdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasMarkdownDraft || markdownModeChanging) return;
        if (MessageBox.Show(Window.GetWindow(this), "Discard your unsaved Markdown changes?", "Discard changes",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetMarkdownBusy(true);
        try
        {
            var anchor = await CaptureMarkdownAnchorAsync();
            // Reload external changes as well, so Discard can recover from a save conflict.
            var file = MarkdownTaskFile.Load(Resource!.Content, MaxPreviewBytes)
                ?? throw new IOException("The file is too large or uses an unsupported encoding.");
            markdownSession?.Refresh();
            markdownTaskFile = file;
            InitializeMarkdownSource(file);
            if (markdownEditorOpen) RestoreSourceAnchor(anchor);
            else await ShowMarkdownPreviewAsync(file.Text, ++previewVersion, true, anchor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MarkdownEditStatus.Text = $"Could not reload: {ex.Message} Your draft has been kept.";
        }
        finally { SetMarkdownBusy(false); }
    }

    private void MarkdownSourceEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SaveMarkdownButton_Click(sender, e);
        }
    }
}
