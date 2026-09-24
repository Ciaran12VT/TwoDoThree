using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using Microsoft.Web.WebView2.Core;
using TwoDoThree.Models;
using TwoDoThree.Services;

namespace TwoDoThree.Controls;

public partial class FileResourcePreviewControl : UserControl
{
    private const int MaxPreviewBytes = 1024 * 1024;
    private const int MaxWordCharacters = 500_000;
    private int previewVersion;
    private string? markdownDocumentId;
    private bool markdownNavigationPending;
    private MarkdownTaskFile? markdownTaskFile;

    public static readonly DependencyProperty ResourceProperty = DependencyProperty.Register(
        nameof(Resource), typeof(ResourceItem), typeof(FileResourcePreviewControl),
        new PropertyMetadata(null, OnResourceChanged));

    public FileResourcePreviewControl()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadPreviewAsync();
        MarkdownWebView.CoreWebView2InitializationCompleted += MarkdownWebView_InitializationCompleted;
        MarkdownWebView.NavigationStarting += MarkdownWebView_NavigationStarting;
    }

    public ResourceItem? Resource
    {
        get => (ResourceItem?)GetValue(ResourceProperty);
        set => SetValue(ResourceProperty, value);
    }

    private static void OnResourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        _ = ((FileResourcePreviewControl)owner).LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        var resource = Resource;
        var version = ++previewVersion;
        markdownDocumentId = null;
        markdownTaskFile = null;
        markdownNavigationPending = false;
        MarkdownWebView.Visibility = Visibility.Collapsed;
        PdfWebView.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Visible;
        if (resource is null)
        {
            NameBlock.Text = DetailsBlock.Text = PreviewTextBox.Text = string.Empty;
            return;
        }

        NameBlock.Text = resource.Name;
        var path = resource.Content;
        PreviewTextBox.Text = "Loading preview…";
        DetailsBlock.Text = path;

        if (resource.Kind == ResourceKind.Folder)
        {
            PreviewTextBox.Text = Directory.Exists(path)
                ? "Select a file in the folder tree to preview it."
                : "The linked folder is missing or inaccessible.";
            return;
        }

        if (!File.Exists(path))
        {
            PreviewTextBox.Text = "The linked file is missing or inaccessible.";
            return;
        }

        try
        {
            var info = new FileInfo(path);
            DetailsBlock.Text = $"{path}\n{info.Length:N0} bytes • Modified {info.LastWriteTime:g}";
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".pdf")
            {
                try
                {
                    await PdfWebView.EnsureCoreWebView2Async();
                    if (version != previewVersion) return;
                    PdfWebView.Source = new Uri(path);
                    PdfWebView.Visibility = Visibility.Visible;
                    PreviewTextBox.Visibility = Visibility.Collapsed;
                }
                catch (Exception)
                {
                    if (version == previewVersion)
                        PreviewTextBox.Text = "PDF preview is unavailable. Use Open to view this file.";
                }
                return;
            }

            if (extension == ".doc")
            {
                PreviewTextBox.Text = "Preview is unavailable for legacy Word .doc files. Use Open to view this file.";
                return;
            }

            MarkdownTaskFile? taskFile = null;
            var result = await Task.Run(() =>
            {
                if (extension is ".md" or ".markdown") taskFile = MarkdownTaskFile.Load(path, MaxPreviewBytes);
                return taskFile?.Text ?? (extension == ".docx" ? ExtractWordText(path) : ReadTextPreview(path));
            });
            if (version != previewVersion) return;
            PreviewTextBox.Text = result;
            if (extension is ".md" or ".markdown")
            {
                markdownTaskFile = taskFile;
                await ShowMarkdownPreviewAsync(result, version, taskFile is not null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException or XmlException or ArgumentException)
        {
            if (version == previewVersion)
                PreviewTextBox.Text = $"Preview is unavailable: {ex.Message}\nUse Open to view this file.";
        }
    }

    private async Task ShowMarkdownPreviewAsync(string markdown, int version, bool editableTasks)
    {
        try
        {
            var documentId = Guid.NewGuid().ToString("N");
            var html = await Task.Run(() => MarkdownPreviewHtml.CreateDisplayDocument(markdown, documentId, editableTasks));
            if (version != previewVersion) return;
            await MarkdownWebView.EnsureCoreWebView2Async();
            if (version != previewVersion) return;
            markdownDocumentId = documentId;
            markdownNavigationPending = true;
            MarkdownWebView.NavigateToString(html);
            MarkdownWebView.Visibility = Visibility.Visible;
            PreviewTextBox.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            if (version != previewVersion) return;
            markdownDocumentId = null;
            markdownNavigationPending = false;
            DetailsBlock.Text += "\nFormatted Markdown preview is unavailable. Showing plain text.";
        }
    }

    private void MarkdownWebView_InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        var core = MarkdownWebView.CoreWebView2;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.WebMessageReceived += MarkdownWebView_WebMessageReceived;
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            OpenMarkdownLink(args.Uri);
        };
    }

    private void MarkdownWebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // NavigateToString reports its initial navigation as a data URI on some runtimes.
        if (markdownNavigationPending && !e.IsUserInitiated
            && (e.Uri == "about:blank" || e.Uri.StartsWith("data:text/html;", StringComparison.Ordinal)))
        {
            markdownNavigationPending = false;
            return;
        }
        if (e.Uri == "about:blank" || e.Uri.StartsWith("about:blank#", StringComparison.Ordinal)) return;
        e.Cancel = true;
        OpenMarkdownLink(e.Uri);
    }

    private static void OpenMarkdownLink(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "mailto")) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // An unavailable external browser should not disrupt the preview.
        }
    }

    private async void MarkdownWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (markdownDocumentId is null || MarkdownWebView.Visibility != Visibility.Visible
            || (e.Source != "about:blank" && !e.Source.StartsWith("about:blank#", StringComparison.Ordinal))) return;
        try
        {
            var request = JsonSerializer.Deserialize<MarkdownPreviewRequest>(e.WebMessageAsJson);
            if (request is null || request.documentId != markdownDocumentId) return;
            if (request.action == "toggleTask")
            {
                var file = markdownTaskFile;
                if (file is null || request.position is null || request.isChecked is null) return;
                string? error = null;
                try
                {
                    await Task.Run(() => file.SetChecked(request.position.Value, request.isChecked.Value));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
                {
                    error = $"Task was not saved: {ex.Message}";
                }
                // A pending save belongs to the original document, even if selection changed meanwhile.
                if (request.documentId != markdownDocumentId) return;
                if (error is null)
                {
                    PreviewTextBox.Text = file.Text;
                    DetailsBlock.Text = $"{Resource?.Content}\nTask saved to the original file.";
                }
                MarkdownWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                {
                    request.documentId, action = "toggleTask", success = error is null, error
                }));
                return;
            }
            if (request.action != "copy" || request.text is null || request.id < 0) return;
            bool success;
            try
            {
                if (request.text.Length == 0) Clipboard.Clear();
                else Clipboard.SetText(request.text);
                success = true;
            }
            catch (ExternalException)
            {
                success = false;
            }
            MarkdownWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                request.documentId, action = "copy", request.id, success
            }));
        }
        catch (JsonException)
        {
            // Ignore messages that do not follow the preview protocol.
        }
    }

    private sealed record MarkdownPreviewRequest(string? documentId, string? action, int id, string? text, int? position, bool? isChecked);

    private static string ReadTextPreview(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int length = (int)Math.Min(stream.Length, MaxPreviewBytes);
        var bytes = new byte[length];
        var count = stream.ReadAtLeast(bytes, length, throwOnEndOfStream: false);
        var span = bytes.AsSpan(0, count);
        bool utf16 = span.StartsWith(new byte[] { 0xFF, 0xFE }) || span.StartsWith(new byte[] { 0xFE, 0xFF });
        if (!utf16 && (span.IndexOf((byte)0) >= 0 || span.CountControlBytes() > Math.Max(8, count / 100)))
            return "This file is not readable as plain text. Use Open to view it in its default application.";

        try
        {
            string text = utf16
                ? (span.StartsWith(new byte[] { 0xFE, 0xFF }) ? new UnicodeEncoding(true, true, true) : new UnicodeEncoding(false, true, true)).GetString(span[2..])
                : new UTF8Encoding(false, true).GetString(span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? span[3..] : span);
            return stream.Length > count ? text + "\n\n[Preview limited to the first 1 MB.]" : text;
        }
        catch (DecoderFallbackException)
        {
            return "This file is not readable as plain text. Use Open to view it in its default application.";
        }
    }

    private static string ExtractWordText(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null) return "This Word document has no readable document text.";

        var builder = new StringBuilder();
        using var xmlStream = entry.Open();
        using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        while (reader.Read() && builder.Length < MaxWordCharacters)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
                continue;
            if (reader.LocalName == "t") builder.Append(reader.ReadString());
            else if (reader.LocalName is "p" or "br") builder.AppendLine();
            else if (reader.LocalName == "tab") builder.Append('\t');
        }

        if (builder.Length >= MaxWordCharacters) builder.AppendLine("\n[Preview truncated.] ");
        return builder.Length == 0 ? "This Word document has no readable document text." : builder.ToString().Trim();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Resource?.Content;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new FileNotFoundException("The linked file or folder is missing or inaccessible.", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Open resource", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

internal static class PreviewByteExtensions
{
    public static int CountControlBytes(this ReadOnlySpan<byte> bytes)
    {
        int count = 0;
        foreach (byte value in bytes)
            if (value < 32 && value is not (9 or 10 or 13)) count++;
        return count;
    }
}
