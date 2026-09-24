using System.Text;
using System.Text.RegularExpressions;
using TwoDoThree.Services;

namespace TwoDoThree.Tests;

public class MarkdownTaskFileTests
{
    [Fact]
    public void RendererMapsOnlyRealTasksToExactSourceLocations()
    {
        const string text = "# 😀\r\n\r\n- [ ] Same\r\n  - [X] Same\r\n\r\n> 1. [x] Quoted\r\n\r\n```md\r\n- [ ] Code\r\n```\r\n\r\nText [ ] not a task\r\n";
        var positions = MarkdownPreviewHtml.GetTaskMarkerPositions(text);
        Assert.Equal(3, positions.Count);
        var html = MarkdownPreviewHtml.CreateDisplayDocument(text, "test", true);
        var inputs = Regex.Matches(html, "<input[^>]+>").Select(m => m.Value).ToArray();
        Assert.Equal(3, inputs.Length);
        Assert.All(inputs, input => Assert.DoesNotContain("disabled", input));
        foreach (var position in positions)
        {
            Assert.Contains(text.Substring(position, 3), new[] { "[ ]", "[X]", "[x]" });
            Assert.Contains($"data-position=\"{position}\"", html);
        }
        Assert.DoesNotContain(text.IndexOf("[ ] Code", StringComparison.Ordinal), positions);
        Assert.DoesNotContain(text.IndexOf("[ ] not", StringComparison.Ordinal), positions);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    public void TogglesPersistOnlyMarkerBytesAndPreserveEncodingAndLineEndings(string format)
    {
        Encoding encoding = format switch
        {
            "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => new UnicodeEncoding(false, true),
            "utf16-be" => new UnicodeEncoding(true, true),
            _ => new UTF8Encoding(false)
        };
        const string text = "# 雪 😀\r\n- [ ] Same\n- [X] Same\r\n- [ ] Same";
        WithFile(Encode(encoding, text), path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            var position = text.IndexOf("[ ]", StringComparison.Ordinal);
            file.SetChecked(position, true);
            var checkedText = text[..(position + 1)] + "x" + text[(position + 2)..];
            Assert.Equal(Encode(encoding, checkedText), File.ReadAllBytes(path));
            file.SetChecked(position, false);
            Assert.Equal(Encode(encoding, text), File.ReadAllBytes(path));
            file.SetChecked(text.IndexOf("[X]", StringComparison.Ordinal), false);
            Assert.Equal(Encode(encoding, text.Replace("[X]", "[ ]")), File.ReadAllBytes(path));
            Assert.Equal(file.Text, MarkdownTaskFile.Load(path, 1024)!.Text);
        });
    }

    [Fact]
    public void ExternalEditOfSameLengthIsNeverOverwritten()
    {
        WithFile(Encoding.UTF8.GetBytes("- [ ] Old"), path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            File.WriteAllText(path, "- [ ] New");
            Assert.Throws<IOException>(() => file.SetChecked(2, true));
            Assert.Equal("- [ ] New", File.ReadAllText(path));
        });
    }

    [Fact]
    public void CodeMarkersAndArbitraryOffsetsCannotBeWritten()
    {
        const string text = "- [ ] Task\n```\n- [ ] Code\n```";
        WithFile(Encoding.UTF8.GetBytes(text), path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            Assert.Throws<ArgumentException>(() => file.SetChecked(text.LastIndexOf("[ ]", StringComparison.Ordinal), true));
            Assert.Throws<ArgumentException>(() => file.SetChecked(0, true));
            Assert.Equal(text, File.ReadAllText(path));
        });
    }

    [Fact]
    public void ReadOnlyFileFailsWithoutChangingItsContents()
    {
        WithFile(Encoding.UTF8.GetBytes("- [ ] Task"), path =>
        {
            var file = MarkdownTaskFile.Load(path, 1024)!;
            File.SetAttributes(path, FileAttributes.ReadOnly);
            Assert.Throws<UnauthorizedAccessException>(() => file.SetChecked(2, true));
            Assert.Equal("- [ ] Task", File.ReadAllText(path));
        });
    }

    [Fact]
    public void LimitedPreviewIsReadOnly()
    {
        WithFile(Encoding.UTF8.GetBytes("- [ ] Task"), path => Assert.Null(MarkdownTaskFile.Load(path, 4)));
        var html = MarkdownPreviewHtml.CreateDisplayDocument("- [ ] Task", "test");
        Assert.Contains(" disabled", Regex.Match(html, "<input[^>]+>").Value);
    }

    private static byte[] Encode(Encoding encoding, string text) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static void WithFile(byte[] bytes, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"markdown-task-{Guid.NewGuid():N}.md");
        try { File.WriteAllBytes(path, bytes); action(path); }
        finally { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
    }
}
