using System.IO;
using System.Reflection;
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
    static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
        app.Startup += async (_, _) =>
        {
            var windows = new List<Window>();
            var directory = Directory.CreateTempSubdirectory("2do3-email-renderer-");
            var path = Path.Combine(directory.FullName, "sample.md");
            File.WriteAllText(path, "# Markdown coexistence\n\n```sql\nSELECT 1;\n```\n");
            try
            {
                // Initialize a real composition resource first: an isolated native email
                // test would miss the incompatible default browser environment regression.
                var emailTask = new TaskItem();
                var emailResource = new ResourceItem { Kind = ResourceKind.Email, Name = "Resource email",
                    FormattedContent = "<h1>Resource email</h1>", Content = "Resource email" };
                emailTask.Resources.Add(emailResource);
                var emailOwner = Detail(emailTask);
                windows.Add(emailOwner);
                emailOwner.Show();
                await WaitUntil(() => Task.FromResult(Descendants(emailOwner).OfType<EmailBodyViewer>().Any()));
                var resourceViewer = Descendants(emailOwner).OfType<EmailBodyViewer>().Single();
                var compositionEmail = (WebView2CompositionControl)resourceViewer.FindName("BodyWebView");
                await Heading(compositionEmail, "Resource email");
                Check(!resourceViewer.UseNativeRenderer && compositionEmail.AllowDrop, "Task email retains composition rendering and drops");

                var messages = new[] {
                    new EmailMessage { Id = "html", Subject = "HTML mail", Body = "Plain fallback", HtmlBody =
                        "<h1>Main email</h1><p><strong>Formatted</strong></p><table><tr><td>Cell</td></tr></table><a href='https://example.com/'>Link</a>" },
                    new EmailMessage { Id = "plain", Subject = "Text mail", Body = "Plain email https://example.com/" }
                };
                var model = new MainViewModel(new AppSettings(), new TestEmailProvider(messages), null!, null!, new NoTaskStore());
                var main = (MainWindow)Activator.CreateInstance(typeof(MainWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, [model], null)!;
                Offscreen(main);
                windows.Add(main);
                main.Show();
                var mainViewer = (EmailBodyViewer)main.FindName("MainEmailBodyViewer");
                var nativeEmail = (WebView2)mainViewer.FindName("NativeBodyWebView");
                await Heading(nativeEmail, "Main email");
                Check(mainViewer.UseNativeRenderer && nativeEmail.IsVisible, "Actual MainWindow renders email with native WebView2");
                Check(!mainViewer.AllowDrop && !nativeEmail.AllowDrop && !nativeEmail.AllowExternalDrop
                    && !((TextBox)mainViewer.FindName("FallbackTextBox")).AllowDrop, "Main email disables browser, WPF and fallback drops");
                Check(((WebView2CompositionControl)mainViewer.FindName("BodyWebView")).Visibility == Visibility.Collapsed,
                    "Main email composition surface stays hidden");
                Check(((TextBox)mainViewer.FindName("FallbackTextBox")).Visibility == Visibility.Collapsed,
                    "Native email initialized without silent plain-text fallback");
                Check(await nativeEmail.ExecuteScriptAsync("document.querySelectorAll('strong,td,a').length") == "3",
                    "Native email retains emphasis, tables and links");
                Check(await nativeEmail.ExecuteScriptAsync("document.querySelector('a').href") == "\"https://example.com/\"",
                    "Email link destination is preserved");
                model.SelectedEmail = messages[1];
                await WaitUntil(async () => await nativeEmail.ExecuteScriptAsync("document.body.textContent.includes('Plain email')") == "true");
                Check(await nativeEmail.ExecuteScriptAsync("document.querySelectorAll('a').length") == "1", "Plain email stays linkified");
                model.SelectedEmail = messages[0];
                await Heading(nativeEmail, "Main email");

                var markdownTask = new TaskItem();
                markdownTask.Resources.Add(new ResourceItem { Kind = ResourceKind.File, Name = "Markdown", Content = path });
                var markdownOwner = Detail(markdownTask);
                windows.Add(markdownOwner);
                markdownOwner.Show();
                await WaitUntil(() => Task.FromResult(Descendants(markdownOwner).OfType<FileResourcePreviewControl>().Any()));
                var embedded = Descendants(markdownOwner).OfType<FileResourcePreviewControl>().Single();
                var compositionMarkdown = (WebView2CompositionControl)embedded.FindName("MarkdownWebView");
                await Heading(compositionMarkdown, "Markdown coexistence");
                Descendants(markdownOwner).OfType<Button>().Single(b => b.ToolTip as string == "Expand Resource")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var popout = markdownOwner.OwnedWindows.OfType<ResourcePopoutWindow>().Single();
                windows.Add(popout);
                popout.UpdateLayout();
                var expanded = Descendants(popout).OfType<FileResourcePreviewControl>().Single();
                var nativeMarkdown = (WebView2)expanded.FindName("NativeMarkdownWebView");
                await Heading(nativeMarkdown, "Markdown coexistence");
                Check(!nativeMarkdown.AllowDrop && !nativeMarkdown.AllowExternalDrop && expanded.UseNativeMarkdownRenderer,
                    "Expanded Markdown remains native without drops");
                Check(nativeEmail.CoreWebView2.Environment.UserDataFolder != compositionEmail.CoreWebView2.Environment.UserDataFolder
                    && nativeEmail.CoreWebView2.Environment.UserDataFolder != nativeMarkdown.CoreWebView2.Environment.UserDataFolder,
                    "Native email, composition and native Markdown use compatible isolated profiles");
                await Heading(nativeEmail, "Main email");
                await Heading(compositionEmail, "Resource email");
                Check(compositionMarkdown.AllowDrop, "Embedded Markdown still accepts drops");
                Drop(compositionEmail, path);
                Check(emailTask.Resources.Any(r => r.Kind == ResourceKind.File && r.Content == path),
                    "File drop over initialized task email still attaches resource");
                Console.WriteLine("PASS: actual main email + resource composition + native Markdown coexistence");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally
            {
                foreach (var window in windows.AsEnumerable().Reverse())
                {
                    foreach (var browser in Descendants(window).OfType<IWebView2>().OfType<IDisposable>().ToList()) browser.Dispose();
                    window.Close();
                }
                File.Delete(path);
                directory.Delete();
                app.Shutdown();
            }
        };
        app.Run();
    }

    static TaskDetailWindow Detail(TaskItem task)
    {
        var window = new TaskDetailWindow(task, [task], [], (_, _) => null, new TagSettings(),
            new Surf2IntegrationSettings(), new Surf2IntegrationService(), new Surf2Launcher());
        Offscreen(window);
        return window;
    }
    static void Offscreen(Window window) { window.Left = -10000; window.ShowActivated = false; window.ShowInTaskbar = false; }
    static Task Heading(IWebView2 browser, string text) => WaitUntil(async () => browser.CoreWebView2 is not null
        && await browser.ExecuteScriptAsync("document.querySelector('h1')?.textContent") == System.Text.Json.JsonSerializer.Serialize(text));
    static void Drop(UIElement target, string path)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, [new DataObject(DataFormats.FileDrop, new[] { path }), DragDropKeyStates.None, DragDropEffects.Copy, target, new Point()], null)!;
        args.RoutedEvent = DragDrop.PreviewDropEvent;
        target.RaiseEvent(args);
        Check(args.Handled && args.Effects == DragDropEffects.Copy, "Task resource drop is intercepted");
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine("PASS: " + description);
    }
    static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Browser condition was not met.");
    }
    sealed class TestEmailProvider(IReadOnlyList<EmailMessage> messages) : IEmailProvider
    {
        public IReadOnlyList<EmailMessage> LoadCachedMessages() => messages;
        public Task<EmailSyncResult> RefreshInboxAsync(EmailSettings settings, bool allowInteractiveSignIn, Window? owner,
            CancellationToken cancellationToken) => Task.FromResult(new EmailSyncResult(messages, true, "Smoke emails"));
    }
    sealed class NoTaskStore : ITaskStore
    {
        public bool IsConfigured => false;
        public IReadOnlyList<TaskItem> LoadTasks() => [];
        public IReadOnlyList<TagResourceCollection> LoadTagResources() => [];
        public void SaveTask(TaskItem task) => throw new InvalidOperationException("Smoke must not persist tasks");
        public void SaveTagResource(string tag, ResourceItem resource, int sortOrder) => throw new InvalidOperationException();
        public void DeleteTask(int taskId) => throw new InvalidOperationException();
    }
}
