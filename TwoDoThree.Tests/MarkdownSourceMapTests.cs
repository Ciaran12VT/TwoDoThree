using TwoDoThree.Services;

namespace TwoDoThree.Tests;

public class MarkdownSourceMapTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void RenderedBlocksCarryOriginalSourceLines(string newline)
    {
        var markdown = string.Join(newline, "# Heading", "", "Paragraph", "continued", "",
            "- First", "- Second", "", "```sql", "SELECT 1;", "SELECT 2;", "```", "");
        var html = MarkdownPreviewHtml.CreateDisplayDocument(markdown, "test", true, true);
        Assert.Matches("<h1[^>]*data-source-start=\"1\"[^>]*data-source-end=\"1\"", html);
        Assert.Matches("<p[^>]*data-source-start=\"3\"[^>]*data-source-end=\"4\"", html);
        Assert.Matches("<li[^>]*data-source-start=\"6\"[^>]*data-source-end=\"6\"", html);
        Assert.Matches("data-source-start=\"9\" data-source-end=\"12\"", html);
        Assert.Contains("SELECT 1;", html);
    }

    [Fact]
    public void DraftPreviewKeepsCopyControlsButDisablesTaskWritesAndEmbedsAnchor()
    {
        var html = MarkdownPreviewHtml.CreateDisplayDocument("- [ ] Task\n\n```\ncode\n```", "draft", false, true,
            new MarkdownViewportAnchor(4.25, .2));
        Assert.Matches("class=\"markdown-task\" disabled", html);
        Assert.Contains("const initialAnchor = {\"line\":4.25,\"viewport\":0.2,\"edge\":null}", html);
        Assert.Contains("button.className = 'copy-code'", html);
    }
}
