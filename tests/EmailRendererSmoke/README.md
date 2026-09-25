# Main email renderer regression check

Run on Windows with .NET 10 and WebView2 installed:

```powershell
dotnet run --project tests/EmailRendererSmoke/EmailRendererSmoke.csproj
```

Opens the actual task-detail email host first, then the actual MainWindow with an isolated model, then an embedded Markdown preview and its real Expand action. It checks simultaneous composition/native initialization, HTML formatting and selection changes, link preservation, main-email drop flags, native Markdown settings, and a routed file drop over the initialized task-resource email browser. No user settings, mailbox, or database are loaded; temporary source files are removed.

Run `tests/MarkdownPopoutSmoke` as well for inline editing, scroll mapping, Save/Discard and draft protection. This checks actual renderer selection and browser behavior, not perceived sharpness at specific display scaling or native Explorer mouse dragging. Link targets are inspected without launching an external browser.
