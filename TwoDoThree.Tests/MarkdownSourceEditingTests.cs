using System.Text;
using TwoDoThree.Services;

namespace TwoDoThree.Tests;

public class MarkdownSourceEditingTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    public void SourceSavePreservesEncodingAndUpdatesCheckboxPositions(string format)
    {
        Encoding encoding = format switch
        {
            "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => new UnicodeEncoding(false, true),
            "utf16-be" => new UnicodeEncoding(true, true),
            _ => new UTF8Encoding(false)
        };
        WithFile([.. encoding.GetPreamble(), .. encoding.GetBytes("Original text\r\n")], path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            const string edited = "# 雪 😀\r\n\n- [ ] New task\n";
            file.SaveText(edited, 1024);
            Assert.Equal([.. encoding.GetPreamble(), .. encoding.GetBytes(edited)], File.ReadAllBytes(path));
            file.SetChecked(edited.IndexOf("[ ]", StringComparison.Ordinal), true);
            Assert.Equal(edited.Replace("[ ]", "[x]"), MarkdownTaskFile.Load(path, 1024)!.Text);
            file.SaveText("", 1024);
            Assert.Equal(encoding.GetPreamble(), File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void StaleSourceEditCannotOverwriteSameLengthExternalChanges()
    {
        WithFile(Encoding.UTF8.GetBytes("old text"), path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            File.WriteAllText(path, "new text");
            Assert.Throws<IOException>(() => file.SaveText("my edited draft", 1024));
            Assert.Equal("new text", File.ReadAllText(path));
            Assert.Equal("old text", file.Text);
        });
    }

    [Fact]
    public void ReadOnlySourceAndOversizedDraftAreNotReplaced()
    {
        WithFile(Encoding.UTF8.GetBytes("original"), path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            Assert.Throws<IOException>(() => file.SaveText(new string('a', 1025), 1024));
            File.SetAttributes(path, FileAttributes.ReadOnly);
            Assert.Throws<UnauthorizedAccessException>(() => file.SaveText("replacement", 1024));
            Assert.Equal("original", File.ReadAllText(path));
        });
    }

    [Fact]
    public void DeletedSourceIsNotSilentlyRecreated()
    {
        WithFile(Encoding.UTF8.GetBytes("original"), path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            File.Delete(path);
            Assert.Throws<FileNotFoundException>(() => file.SaveText("replacement", 1024));
            Assert.False(File.Exists(path));
        });
    }

    private static void WithFile(byte[] bytes, Action<string> action)
    {
        var directory = Directory.CreateTempSubdirectory("2do3-markdown-edit-");
        var path = Path.Combine(directory.FullName, "source.md");
        try
        {
            File.WriteAllBytes(path, bytes);
            action(path);
            Assert.DoesNotContain(Directory.EnumerateFiles(directory.FullName), file => file.EndsWith(".tmp"));
        }
        finally
        {
            if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
            directory.Delete();
        }
    }
}
