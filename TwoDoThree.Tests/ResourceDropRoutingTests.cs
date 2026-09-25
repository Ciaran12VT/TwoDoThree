using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;
using TwoDoThree.Controls;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;
using TwoDoThree.Views;

namespace TwoDoThree.Tests;

public class ResourceDropRoutingTests
{
    [Fact]
    public void FileDropsTunnelFromTreePreviewAndBrowserSurfaceIntoTaskResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                var app = new App();
                app.InitializeComponent();
                var task = new TaskItem();
                var window = new TaskDetailWindow(task, [task], [], (_, _) => null,
                    new TagSettings(), new Surf2IntegrationSettings(), null!, null!);
                var model = (TaskDetailViewModel)window.DataContext;
                var previewResource = new ResourceItem { Kind = ResourceKind.File, Name = "Preview", Content = path };
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1120, 820));
                content.Arrange(new Rect(0, 0, 1120, 820));
                content.UpdateLayout();

                foreach (var surface in new[] { "tree", "preview", "browser", "header" })
                {
                    model.SelectedResource = previewResource;
                    content.UpdateLayout();
                    var viewer = (ResourceViewerControl)window.FindName("SelectedResourceViewer");
                    var preview = Descendants(viewer).OfType<FileResourcePreviewControl>().Single();
                    UIElement target = surface switch
                    {
                        "tree" => (TreeView)window.FindName("ResourceTree"),
                        "browser" => (WebView2CompositionControl)preview.FindName("PdfWebView"),
                        "header" => (DockPanel)window.FindName("ResourcesHeader"),
                        _ => preview
                    };
                    Assert.True(target.AllowDrop, $"AllowDrop is disabled on {surface}.");
                    var data = new DataObject(DataFormats.FileDrop, new[] { path });
                    // WPF constructs these internally during an OLE drag. Raise the actual routed event
                    // to verify interception above editors and composition browser controls.
                    var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null, [data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point()], null)!;
                    args.RoutedEvent = DragDrop.PreviewDropEvent;
                    target.RaiseEvent(args);
                    Assert.True(args.Handled, $"Drop was not intercepted on {surface}.");
                    Assert.Equal(DragDropEffects.Copy, args.Effects);
                }
                Assert.Single(task.Resources, r => r.Kind == ResourceKind.File);
                window.Close();
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
            finally { System.IO.File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF drop routing smoke test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
