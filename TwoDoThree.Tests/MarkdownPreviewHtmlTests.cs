using TwoDoThree.Services;

namespace TwoDoThree.Tests;

public class MarkdownPreviewHtmlTests
{
    [Fact]
    public void RendersDocumentFormatting()
    {
        var html = MarkdownPreviewHtml.CreateDisplayDocument("""
            # Heading

            **Bold**, *italic*, ~~deleted~~ and `inline`.

            - First
            - Second

            > Quoted

            [Website](https://example.com)

            | Name | Value |
            | --- | --- |
            | One | Two |

            - [x] Done
            """, "test");

        Assert.Contains("<h1 id=\"heading\">Heading</h1>", html);
        Assert.Contains("<strong>Bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<del>deleted</del>", html);
        Assert.Contains("<code>inline</code>", html);
        Assert.Contains("<li>First</li>", html);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<td>Two</td>", html);
        Assert.Contains("type=\"checkbox\"", html);
    }

    [Theory]
    [InlineData("```csharp\n  var text = \"<tag>&\";\n```", "  var text = &quot;&lt;tag&gt;&amp;&quot;;\n")]
    [InlineData("    first\n      second", "first\n  second\n")]
    [InlineData("~~~\n雪 😀\n~~~", "雪 😀\n")]
    [InlineData("```\n```", "")]
    public void RendersCodeAsTextPreservingWhitespace(string markdown, string expected)
    {
        var html = MarkdownPreviewHtml.CreateDisplayDocument(markdown, "test");
        Assert.Contains("<pre><code", html);
        Assert.Contains($">{expected}</code></pre>", html);
    }

    [Fact]
    public void EmbeddedHtmlCannotCreateExecutableElements()
    {
        var html = MarkdownPreviewHtml.CreateDisplayDocument("""
            <script>alert('unsafe')</script>
            <img src=x onerror="alert('unsafe')">
            <button onclick="alert('unsafe')">Click</button>
            """, "test");

        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<button onclick", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void DocumentIdentifierCannotEscapeTrustedScript()
    {
        var html = MarkdownPreviewHtml.CreateDisplayDocument("", "</script><script>unsafe</script>");
        Assert.DoesNotContain("</script><script>unsafe", html);
        Assert.Contains("default-src 'none'", html);
    }
}
