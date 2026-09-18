using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using TwoDoThree.Models;

namespace TwoDoThree.Controls;

public partial class FileResourcePreviewControl : UserControl
{
    private const int MaxPreviewBytes = 1024 * 1024;
    private const int MaxWordCharacters = 500_000;
    private int previewVersion;

    public static readonly DependencyProperty ResourceProperty = DependencyProperty.Register(
        nameof(Resource), typeof(ResourceItem), typeof(FileResourcePreviewControl),
        new PropertyMetadata(null, OnResourceChanged));

    public FileResourcePreviewControl()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadPreviewAsync();
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
        if (resource is null) return;

        NameBlock.Text = resource.Name;
        var path = resource.Content;
        PdfWebView.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Visible;
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

            var result = await Task.Run(() => extension == ".docx" ? ExtractWordText(path) : ReadTextPreview(path));
            if (version == previewVersion) PreviewTextBox.Text = result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException or XmlException or ArgumentException)
        {
            if (version == previewVersion)
                PreviewTextBox.Text = $"Preview is unavailable: {ex.Message}\nUse Open to view this file.";
        }
    }

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
