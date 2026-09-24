using Markdig;

namespace TwoDoThree.Services;

public static class MarkdownPreviewHtml
{
    // Enable document formatting without allowing embedded HTML or arbitrary attributes.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    public static string CreateDisplayDocument(string markdown, string documentId)
    {
        var body = Markdown.ToHtml(markdown, Pipeline);
        var nonce = Guid.NewGuid().ToString("N");
        var documentIdJson = System.Text.Json.JsonSerializer.Serialize(documentId);
        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: http: data:; style-src 'nonce-{{nonce}}'; script-src 'nonce-{{nonce}}'; base-uri 'none'; form-action 'none'">
<style nonce="{{nonce}}">
body { margin: 0; padding: 20px; color: #111827; background: white; font: 14px/1.6 'Segoe UI', sans-serif; overflow-wrap: anywhere; }
h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.3em 0 .6em; }
h1, h2 { border-bottom: 1px solid #e5e7eb; padding-bottom: .3em; }
body > :first-child { margin-top: 0; }
a { color: #2563eb; }
blockquote { margin-left: 0; padding-left: 16px; border-left: 4px solid #cbd5e1; color: #475569; }
img { max-width: 100%; height: auto; }
table { display: block; overflow-x: auto; border-collapse: collapse; margin: 1em 0; }
th, td { padding: 6px 12px; border: 1px solid #cbd5e1; }
th { background: #f1f5f9; }
hr { border: 0; border-top: 1px solid #cbd5e1; }
code { font-family: Consolas, monospace; background: #f1f5f9; padding: 2px 4px; border-radius: 3px; }
.code-block { position: relative; margin: 1em 0; border: 1px solid #e2e8f0; border-radius: 6px; background: #f8fafc; }
pre { margin: 0; padding: 44px 14px 14px; overflow-x: auto; white-space: pre; overflow-wrap: normal; tab-size: 4; }
pre code { padding: 0; background: transparent; }
.copy-code { position: absolute; top: 7px; right: 7px; padding: 3px 10px; border: 1px solid #cbd5e1; border-radius: 4px; background: white; color: #334155; font: 12px/1.6 'Segoe UI', sans-serif; cursor: pointer; }
.copy-code:hover { background: #e2e8f0; }
.copy-code:focus-visible { outline: 2px solid #2563eb; outline-offset: 2px; }
</style>
</head>
<body>
{{body}}
<script nonce="{{nonce}}">
const documentId = {{documentIdJson}};
const buttons = [];
document.querySelectorAll('pre > code').forEach((code, id) => {
    const pre = code.parentElement;
    const wrapper = document.createElement('div');
    wrapper.className = 'code-block';
    pre.replaceWith(wrapper);
    wrapper.append(pre);
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'copy-code';
    button.textContent = 'Copy';
    button.setAttribute('aria-label', 'Copy code to clipboard');
    button.setAttribute('aria-live', 'polite');
    button.addEventListener('click', () => {
        button.disabled = true;
        window.chrome.webview.postMessage({ documentId, id, text: code.textContent });
    });
    wrapper.append(button);
    buttons.push(button);
});
window.chrome.webview.addEventListener('message', ({ data }) => {
    if (data.documentId !== documentId) return;
    const button = buttons[data.id];
    if (!button) return;
    button.textContent = data.success ? 'Copied!' : 'Copy failed';
    button.setAttribute('aria-label', data.success ? 'Code copied to clipboard' : 'Copy failed. Try again.');
    setTimeout(() => {
        button.textContent = 'Copy';
        button.setAttribute('aria-label', 'Copy code to clipboard');
        button.disabled = false;
    }, 1500);
});
</script>
</body>
</html>
""";
    }
}
