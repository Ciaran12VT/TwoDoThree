using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using TwoDoThree.Models;
using TwoDoThree.Services;

namespace TwoDoThree.Controls;

public partial class FileResourcePreviewControl
{
    private MarkdownDocumentSession? markdownSession;
    private List<MarkdownPin> draftPins = [];
    private readonly DispatcherTimer markdownRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool refreshingMarkdownSession;
    private bool applyingSessionChange;
    private bool pinSidebarOpen;
    private double pinSidebarScroll;
    private static readonly JsonSerializerOptions PinJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private void InitializePinIntegration()
    {
        MarkdownSourceEditor.Document.Changed += (_, change) =>
        {
            if (markdownSourceInitialized)
                draftPins = draftPins.Select(p => MarkdownPinAnchors.ApplyEdit(p, change.Offset, change.RemovalLength,
                    change.InsertionLength, MarkdownSourceEditor.Text)).ToList();
        };
        markdownRefreshTimer.Tick += async (_, _) =>
        {
            if (refreshingMarkdownSession || markdownSession is null || markdownModeChanging) return;
            refreshingMarkdownSession = true;
            try { await Task.Run(markdownSession.Refresh); }
            finally { refreshingMarkdownSession = false; }
        };
    }

    private void AttachMarkdownSession(string path)
    {
        var session = MarkdownDocumentSession.Get(path);
        if (!ReferenceEquals(session, markdownSession))
        {
            DetachMarkdownSession();
            markdownSession = session;
            session.Changed += MarkdownSession_Changed;
        }
        session.Refresh();
        if (IsLoaded) markdownRefreshTimer.Start();
    }

    private void DetachMarkdownSession()
    {
        markdownRefreshTimer.Stop();
        if (markdownSession is not null) markdownSession.Changed -= MarkdownSession_Changed;
        markdownSession = null;
    }

    private MarkdownPinPresentation PinPresentation(bool editable) => new(
        HasMarkdownDraft ? draftPins : markdownSession?.Pins ?? [], editable && markdownSession?.Text is not null,
        pinSidebarOpen, pinSidebarScroll, markdownSession?.Error);

    private async Task CapturePinViewAsync()
    {
        if (!UseNativeMarkdownRenderer || MarkdownBrowser.CoreWebView2 is null) return;
        try
        {
            var state = JsonSerializer.Deserialize<JsonElement>(await MarkdownBrowser.ExecuteScriptAsync(
                "typeof markdownPinViewState === 'function' ? markdownPinViewState() : null"));
            if (state.ValueKind == JsonValueKind.Object)
            {
                pinSidebarOpen = state.GetProperty("open").GetBoolean();
                pinSidebarScroll = state.GetProperty("scroll").GetDouble();
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private void PostPinUpdate(string? error = null, bool created = false)
    {
        if (!UseNativeMarkdownRenderer || markdownDocumentId is null || MarkdownBrowser.CoreWebView2 is null) return;
        MarkdownBrowser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            documentId = markdownDocumentId, action = "pins", pins = HasMarkdownDraft ? draftPins : markdownSession?.Pins ?? [], error, created
        }, PinJson));
    }

    private void PostTaskSync(int position, bool value)
    {
        if (markdownDocumentId is null || MarkdownBrowser.CoreWebView2 is null) return;
        MarkdownBrowser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            documentId = markdownDocumentId, action = "taskSync", position, isChecked = value
        }));
    }

    private void MarkdownSession_Changed(object? sender, MarkdownDocumentChange change)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (!ReferenceEquals(sender, markdownSession) || applyingSessionChange || markdownModeChanging) return;
            try
            {
                var text = markdownSession!.Text;
                if (HasMarkdownDraft)
                {
                    draftPins = markdownSession.Pins.Select(p => draftPins.SingleOrDefault(d => d.Id == p.Id)
                        ?? MarkdownPinAnchors.Resolve(p, MarkdownSourceEditor.Text, false)).ToList();
                    if (text != markdownTaskFile?.Text) MarkdownEditStatus.Text = "The source changed in another window or outside the app. Your draft is kept; Save will check for conflicts.";
                    PostPinUpdate();
                    return;
                }
                if (text == markdownTaskFile?.Text)
                {
                    if (change.TaskPosition is { } syncedPosition && text == change.Text) PostTaskSync(syncedPosition, change.Checked!.Value);
                    draftPins = markdownSession.Pins; PostPinUpdate(); return;
                }
                if (change.TaskPosition is not null && text is not null && markdownTaskFile is not null
                    && MarkdownDocumentSession.OnlyTaskMarkersChanged(markdownTaskFile.Text, text))
                {
                    var anchor = markdownEditorOpen ? CaptureSourceAnchor() : null;
                    var prior = markdownTaskFile.Text;
                    var snapshot = markdownSession.LoadSnapshot();
                    if (snapshot?.Text == text)
                    {
                        markdownTaskFile = snapshot;
                        InitializeMarkdownSource(markdownTaskFile);
                        if (anchor is not null) RestoreSourceAnchor(anchor);
                        foreach (var position in MarkdownPreviewHtml.GetTaskMarkerPositions(text))
                            if (prior[position + 1] != text[position + 1]) PostTaskSync(position, text[position + 1] is 'x' or 'X');
                        return;
                    }
                }
                SetMarkdownBusy(true);
                try
                {
                    var viewport = await CaptureMarkdownAnchorAsync();
                    markdownDocumentId = null;
                    markdownTaskFile = markdownSession.LoadSnapshot();
                    InitializeMarkdownSource(markdownTaskFile);
                    if (markdownEditorOpen) RestoreSourceAnchor(viewport);
                    else await ShowMarkdownPreviewAsync(markdownTaskFile?.Text ?? "# Source unavailable\nThe linked Markdown file is missing or inaccessible.",
                        ++previewVersion, markdownTaskFile is not null, viewport);
                }
                finally { SetMarkdownBusy(false); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                MarkdownEditStatus.Text = "Could not refresh the source: " + e.Message;
            }
        });
    }

    private bool HandlePinMessage(MarkdownPreviewRequest request)
    {
        if (request.action is not ("pinSelection" or "unpin")) return false;
        if (!UseNativeMarkdownRenderer || markdownSession is null) return true;
        try
        {
            if (request.action == "unpin")
            {
                if (request.pinId is not null) markdownSession.Unpin(request.pinId);
            }
            else
            {
                if (HasMarkdownDraft || markdownTaskFile is null) throw new IOException("Save the document before pinning a selection.");
                markdownSession.Refresh();
                markdownSession.AddPin(markdownTaskFile.Text, request.start ?? -1, request.end ?? -1, request.text ?? "");
                pinSidebarOpen = true;
            }
            PostPinUpdate(created: request.action == "pinSelection");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PostPinUpdate(e.Message);
        }
        return true;
    }
}
