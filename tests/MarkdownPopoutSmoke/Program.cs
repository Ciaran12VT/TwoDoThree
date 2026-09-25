using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using ICSharpCode.AvalonEdit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;
using TwoDoThree;
using TwoDoThree.Controls;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TwoDoThree.ViewModels;
using TwoDoThree.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Trace.Listeners.Add(new ConsoleTraceListener());
        var app = new App();
        app.InitializeComponent();
        // Load production styles without starting MainWindow or reading user task data.
        typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
        app.Startup += async (_, _) =>
        {
            var directory = Directory.CreateTempSubdirectory("2do3-popout-smoke-");
            var path = Path.Combine(directory.FullName, "sample.md");
            File.WriteAllText(path, "# Rendered heading\n\n- [ ] Saved task\n\n```sql\nSELECT 1;\n```\n"
                + string.Concat(Enumerable.Range(1, 80).Select(i => $"\n## Section {i}\n\nParagraph with **formatted passage {i}** and a [link](https://example.com).\n\n- Item {i}\n- Another item\n\n```sql\nSELECT {i};\nSELECT 'second line';\n```\n")));
            TaskDetailWindow? owner = null;
            var popouts = new List<ResourcePopoutWindow>();
            try
            {
                var resource = new ResourceItem { Kind = ResourceKind.File, Name = "Markdown", Content = path };
                var task = new TaskItem();
                task.Resources.Add(resource);
                owner = new TaskDetailWindow(task, [task], [], (_, _) => null, new TagSettings(),
                    new Surf2IntegrationSettings(), new Surf2IntegrationService(), new Surf2Launcher())
                    { Left = -10000, ShowActivated = false, ShowInTaskbar = false };
                ((TaskDetailViewModel)owner.DataContext).SelectedResource = resource;
                owner.Show();
                await WaitUntil(() => Task.FromResult(Descendants(owner).OfType<FileResourcePreviewControl>().Any()));
                var embedded = Descendants(owner).OfType<FileResourcePreviewControl>().Single();
                var composition = (WebView2CompositionControl)embedded.FindName("MarkdownWebView");
                await Rendered(composition, embedded);
                Check(!embedded.UseNativeMarkdownRenderer && composition.AllowDrop, "Embedded view retains composition rendering and drops");
                Check(((Button)embedded.FindName("EditMarkdownButton")).Visibility == Visibility.Collapsed
                    && ((Button)embedded.FindName("OpenButton")).IsVisible, "Embedded Edit is absent and Open remains available");

                var expand = Descendants(owner).OfType<Button>().Single(b => b.ToolTip as string == "Expand Resource");
                // The embedded browser MUST initialize first. Opening a popout alone misses
                // the incompatible native/composition default-environment regression.
                for (int index = 0; index < 2; index++)
                {
                    expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var popout = owner.OwnedWindows.OfType<ResourcePopoutWindow>().Single(w => !popouts.Contains(w));
                    popouts.Add(popout);
                    popout.UpdateLayout();
                    var preview = Descendants(popout).OfType<FileResourcePreviewControl>().Single();
                    var native = (WebView2)preview.FindName("NativeMarkdownWebView");
                    native.CoreWebView2InitializationCompleted += (_, e) =>
                        Console.WriteLine($"Native initialized: {e.IsSuccess}; {e.InitializationException}");
                    // Do not assign preview.Resource or force a reload: test the normal binding lifecycle.
                    await Rendered(native, preview);
                    Check(preview.UseNativeMarkdownRenderer && !native.AllowDrop && !native.AllowExternalDrop,
                        "Expanded view renders through native WebView2 without drops");
                    Check(native.CoreWebView2.Environment.UserDataFolder != composition.CoreWebView2.Environment.UserDataFolder,
                        "Native and composition browsers use separate user-data folders");
                    Check(await native.ExecuteScriptAsync("document.querySelectorAll('.copy-code').length") == "81",
                        "Code copy control is rendered");
                    Check(((TextBox)preview.FindName("PreviewTextBox")).Visibility == Visibility.Collapsed,
                        "Plain-text fallback is hidden");
                }

                var firstPreview = Descendants(popouts[0]).OfType<FileResourcePreviewControl>().Single();
                var firstBrowser = (WebView2)firstPreview.FindName("NativeMarkdownWebView");
                await firstBrowser.ExecuteScriptAsync("document.querySelector('.markdown-task').click()");
                await WaitUntil(async () => await firstBrowser.ExecuteScriptAsync("document.getElementById('task-status').textContent") == "\"Task saved.\"");
                Check(File.ReadAllText(path).Contains("[x] Saved task"), "Native checkbox saves to the original file");
                var secondPreview = Descendants(popouts[1]).OfType<FileResourcePreviewControl>().Single();
                await PinChecks.Run(firstPreview, firstBrowser, (WebView2)secondPreview.FindName("NativeMarkdownWebView"), path);
                await CheckInlineEditing(firstPreview, firstBrowser, path);
                await Rendered(composition, embedded);
                Console.WriteLine("PASS: embedded-first initialization and simultaneous native popouts");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
                foreach (var popout in popouts)
                {
                    // If an assertion fails during a dirty draft, reset it for unattended cleanup.
                    var preview = Descendants(popout).OfType<FileResourcePreviewControl>().SingleOrDefault();
                    if (preview is not null)
                    {
                        var file = (MarkdownTaskFile?)typeof(FileResourcePreviewControl).GetField("markdownTaskFile", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview);
                        ((TextEditor)preview.FindName("MarkdownSourceEditor")).Text = file?.Text ?? "";
                    }
                    DisposeBrowsers(popout); popout.Close();
                }
                if (owner is not null) { DisposeBrowsers(owner); owner.Close(); }
                File.Delete(path);
                var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
                File.Delete(Path.Combine(AppStoragePaths.MarkdownPinsDirectory, key + ".json"));
                directory.Delete();
                app.Shutdown();
            }
        };
        app.Run();
    }

    private static async Task CheckInlineEditing(FileResourcePreviewControl preview, WebView2 browser, string path)
    {
        var toggle = (Button)preview.FindName("EditMarkdownButton");
        var save = (Button)preview.FindName("SaveMarkdownButton");
        var editor = (TextEditor)preview.FindName("MarkdownSourceEditor");
        var status = (TextBlock)preview.FindName("MarkdownEditStatus");
        async Task<MarkdownViewportAnchor> BrowserAnchor() => JsonSerializer.Deserialize<MarkdownViewportAnchor>(
            await browser.ExecuteScriptAsync("captureMarkdownAnchor()"))!;
        async Task ToSource()
        {
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => Task.FromResult(editor.IsVisible && toggle.IsEnabled));
        }
        async Task ToPreview()
        {
            var oldId = await browser.ExecuteScriptAsync("documentId");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(async () => !editor.IsVisible && toggle.IsEnabled
                && await browser.ExecuteScriptAsync("documentId") != oldId);
            await Task.Delay(100); // Let the browser's first animation frame restore the anchor.
        }
        double SourceLine()
        {
            var view = editor.TextArea.TextView;
            var y = editor.VerticalOffset + view.ActualHeight * .2;
            var line = view.GetDocumentLineByVisualTop(y);
            return line.LineNumber + (y - view.GetVisualTopByDocumentLine(line.LineNumber)) / view.DefaultLineHeight;
        }

        foreach (var selector in new[] { "#section-20", "li[data-source-start]", ".code-block[data-source-start]" })
        {
            await browser.ExecuteScriptAsync($"{{ const matches = document.querySelectorAll({JsonSerializer.Serialize(selector)}); const e = matches[Math.floor(matches.length/2)]; scrollTo(0, scrollY + e.getBoundingClientRect().top - innerHeight*.2); }}");
            var anchor = await BrowserAnchor();
            var windowCount = Application.Current.Windows.Count;
            await ToSource();
            Check(Application.Current.Windows.Count == windowCount, "Edit stays inside the expanded window");
            if (anchor.edge is null) Check(Math.Abs(SourceLine() - anchor.line) < 2, $"Rendered passage maps to source: {selector}");
            var sourceLine = SourceLine();
            await ToPreview();
            var restored = await BrowserAnchor();
            if (restored.edge is null) Check(Math.Abs(restored.line - sourceLine) < 2, $"Source passage maps back to rendered view: {selector}");
        }

        await browser.ExecuteScriptAsync("document.getElementById('section-40').scrollIntoView()");
        await ToSource();
        var disk = File.ReadAllText(path);
        editor.Document.Insert(0, "<!-- inserted above -->\n\n");
        var editOffset = editor.Document.GetLineByNumber((int)SourceLine()).Offset;
        editor.Document.Insert(editOffset, "New text near current passage.\n");
        var expectedLine = SourceLine();
        await ToPreview();
        Check(File.ReadAllText(path) == disk, "Previewing a draft does not save it");
        Check(Math.Abs((await BrowserAnchor()).line - expectedLine) < 3, "Edits above and near the viewport retain the corresponding passage");
        Check(await browser.ExecuteScriptAsync("document.body.textContent.includes('New text near current passage.')") == "true", "Unsaved source renders as Markdown");
        Check(await browser.ExecuteScriptAsync("document.querySelectorAll('.markdown-task:not(:disabled)').length") == "0", "Unsaved draft disables direct checkbox writes");
        await ToSource();
        Check(editor.Text.Contains("New text near current passage."), "Draft survives switching both directions");
        var popout = Window.GetWindow(preview);
        await AnswerDialog(2, () => popout.Close());
        Check(popout.IsVisible && editor.Text.Contains("New text near current passage."), "Cancel closing keeps the window and draft");
        await AnswerDialog(2, () => popout.Owner.Close());
        Check(popout.Owner.IsVisible && popout.IsVisible, "Closing the owner also protects the draft");
        var viewer = Descendants(popout).OfType<ResourceViewerControl>().Single();
        var priorResource = viewer.Resource;
        await AnswerDialog(2, () => viewer.Resource = new ResourceItem { Kind = ResourceKind.Text, Content = "Other resource" });
        Check(ReferenceEquals(viewer.Resource, priorResource) && editor.IsVisible, "Cancel changing resource kind preserves the inline draft");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(() => Task.FromResult(status.Text.Contains("Could not save:")));
        Check(editor.Text.Contains("New text near current passage.") && File.ReadAllText(path) == disk, "Failed inline Save retains draft and original bytes");
        await AnswerDialog(6, () => popout.Close());
        Check(popout.IsVisible && status.Text.Contains("Could not save:"), "Failed Save during closing keeps the draft open");
        File.SetAttributes(path, FileAttributes.Normal);
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(() => Task.FromResult(File.ReadAllText(path) == editor.Text));
        Check(editor.IsVisible, "Save stays in the selected mode");
        await ToPreview();
        Check(await browser.ExecuteScriptAsync("document.querySelectorAll('.markdown-task:not(:disabled)').length") == "1", "Saved preview restores task checkbox interaction");
        await browser.ExecuteScriptAsync("scrollTo(0, document.documentElement.scrollHeight)");
        await ToSource();
        await ToPreview();
        Check((await BrowserAnchor()).edge == "end", "End-of-document position survives both modes");
        await browser.ExecuteScriptAsync("scrollTo(0, 0)");
        await ToSource();
        await ToPreview();
        Check((await BrowserAnchor()).edge == "start", "Beginning-of-document position survives both modes");
        await ToSource();
        editor.Document.Insert(0, "Unsaved discard test\n");
        await AnswerDialog(6, () => ((Button)preview.FindName("DiscardMarkdownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), "Discard changes");
        await WaitUntil(() => Task.FromResult(editor.Text == File.ReadAllText(path)));
        Check(!editor.Text.Contains("Unsaved discard test"), "Explicit Discard reloads the source file");
        editor.Document.Insert(0, "Save from preview\n\n");
        await ToPreview();
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(() => Task.FromResult(File.ReadAllText(path) == editor.Text && toggle.IsEnabled));
        Check(browser.IsVisible && !editor.IsVisible, "Saving an unsaved preview keeps rendered mode selected");
        var previousClipboard = Clipboard.GetDataObject();
        try
        {
            await browser.ExecuteScriptAsync("document.querySelector('.copy-code').click()");
            await WaitUntil(async () => await browser.ExecuteScriptAsync("document.querySelector('.copy-code').textContent") == "\"Copied!\"");
            Check(Clipboard.GetText() == "SELECT 1;\n", "Code copy works after inline edit, preview and save");
        }
        finally
        {
            if (previousClipboard is null) Clipboard.Clear();
            else Clipboard.SetDataObject(previousClipboard, true);
        }
        await ToSource();
        editor.Document.Insert(0, "Conflicting local draft\n");
        File.AppendAllText(path, "\nExternal edit\n");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(() => Task.FromResult(status.Text.Contains("changed outside")));
        Check(editor.Text.StartsWith("Conflicting local draft") && !File.ReadAllText(path).Contains("Conflicting local draft"),
            "External-edit conflict preserves both the draft and disk changes");
        await AnswerDialog(6, () => ((Button)preview.FindName("DiscardMarkdownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), "Discard changes");
        await WaitUntil(() => Task.FromResult(editor.Text == File.ReadAllText(path)));
        Check(editor.Text.EndsWith("External edit\n"), "Discard after a conflict loads the external changes");
    }

    // Answer only this smoke process's modal prompt; never target another application.
    internal static async Task AnswerDialog(int answer, Action trigger, string title = "Unsaved Markdown changes")
    {
        var responder = Task.Run(async () =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var dialog = FindWindow("#32770", title);
                GetWindowThreadProcessId(dialog, out var process);
                if (dialog != IntPtr.Zero && process == Environment.ProcessId)
                {
                    SendMessage(dialog, 0x0111, new IntPtr(answer), IntPtr.Zero);
                    return;
                }
                await Task.Delay(50);
            }
            throw new TimeoutException("Expected draft confirmation dialog did not appear.");
        });
        trigger();
        await responder;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static async Task Rendered(IWebView2 browser, FileResourcePreviewControl preview)
    {
        await WaitUntil(async () =>
        {
            var details = ((TextBlock)preview.FindName("DetailsBlock")).Text;
            if (details.Contains("Formatted Markdown preview is unavailable")) throw new Exception(details);
            return browser.CoreWebView2 is not null
                && await browser.ExecuteScriptAsync("document.querySelector('h1')?.textContent") == "\"Rendered heading\"";
        });
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition().WaitAsync(TimeSpan.FromSeconds(10))) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Markdown did not render through the normal preview lifecycle.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void DisposeBrowsers(DependencyObject window)
    {
        foreach (var browser in Descendants(window).OfType<IWebView2>().ToList())
            ((IDisposable)browser).Dispose();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
