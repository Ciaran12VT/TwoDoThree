# Markdown popout regression check

Run on Windows with .NET 10 and the WebView2 Runtime installed:

```powershell
dotnet run --project tests/MarkdownPopoutSmoke/MarkdownPopoutSmoke.csproj
```

This launches the real task-detail window offscreen with temporary Markdown, waits for its composition preview, and invokes the actual Expand Resource button twice. It verifies formatted Markdown in both native popouts, separate browser user-data folders, drop settings, code-copy controls and checkbox file persistence. It fails on initialization errors or plain-text fallback. It never opens the main window or accesses the user's task database.

The inline-edit checks use a long document with headings, lists and code blocks. They verify source-line/viewport anchors in both directions, edits above and near the viewport, beginning/end clamping, unsaved draft previews, Save in either mode, read-only and external-edit failures, and Discard. They also verify that embedded Edit is absent while Open remains available. Modal draft confirmations are answered only when their window belongs to this smoke process, covering popout close, owner close, resource-kind changes and failed Save during closing. The clipboard is temporarily used to verify exact code copying and then restored.

The pin checks cover exact partial formatting, multi-block selections, local persistence, overlapping checklist excerpts, multiwindow updates, independent scrolling, header-only navigation, highlight visibility, unpin, provisional draft anchors, save/discard, ambiguous external edits, and missing/restored files. Pin metadata created for the temporary source is removed after the check.

Internal dragging is exercised using DevTools mouse input to initiate a real browser selection drag, browser-generated intercepted drag data, and DevTools drag events. The checks cover formatted text, checklist text, code text, delayed left-edge reveal/cancellation, overlay positioning and successful drop. This validates the native WebView2 browser drag path with external drops disabled; it is not a physical mouse/OLE test on the user's display. A final manual check with the user's mouse/display scaling remains useful.

Opening a native popout by itself cannot catch the shared-environment initialization failure. Do not replace the embedded-first sequence with an isolated preview, or force a resource reload after opening. This checks rendering functionality, not perceived sharpness at a particular display scaling.
