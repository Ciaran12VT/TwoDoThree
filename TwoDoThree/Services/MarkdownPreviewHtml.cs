using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace TwoDoThree.Services;

public static class MarkdownPreviewHtml
{
    private static string Asset(string name)
    {
        using var stream = typeof(MarkdownPreviewHtml).Assembly.GetManifestResourceStream("TwoDoThree.Assets." + name)!;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static readonly string PinScript = Asset("MarkdownPins.js");
    private static readonly string PinStyles = Asset("MarkdownPins.css");
    private const string PinMarkup = """
<nav class="pin-toolbar" aria-label="Pinned excerpts">
  <button id="pin-toggle" aria-label="Toggle pinned excerpts" title="Show or hide pinned excerpts" aria-controls="pin-sidebar" aria-expanded="false"><svg aria-hidden="true" viewBox="0 0 24 24"><path d="M4 6h16M4 12h16M4 18h16"/></svg></button>
</nav>
<aside id="pin-sidebar" aria-label="Pinned excerpts">
  <div class="pin-sidebar-controls">
    <label class="pin-wrap-control" title="Wrap pinned text and code to the sidebar width"><input id="pin-wrap" type="checkbox" checked> Wrap text</label>
    <button id="pin-clear" type="button" title="Clear all pins for this document">Clear all pins</button>
  </div>
  <p id="pin-status" role="status"></p><div id="pin-list"></div>
</aside>
<div id="pin-divider" role="separator" aria-label="Resize pinned excerpts" aria-orientation="vertical" tabindex="0"></div>
<div id="pin-menu" class="pin-context-menu" role="menu" hidden><button id="pin-selection" role="menuitem" aria-keyshortcuts="Alt+Shift+P">Pin selected text <kbd>Alt+Shift+P</kbd></button></div>
<div id="pin-card-menu" class="pin-context-menu" role="menu" hidden><button id="pin-rename" role="menuitem">Rename</button></div>
<dialog id="pin-rename-dialog" aria-labelledby="pin-rename-heading">
  <form id="pin-rename-form">
    <h2 id="pin-rename-heading">Rename pin</h2>
    <label for="pin-title-input">Title</label><input id="pin-title-input" maxlength="200" required autocomplete="off">
    <p id="pin-title-error" role="alert"></p>
    <div class="pin-dialog-actions"><button id="pin-rename-cancel" type="button">Cancel</button><button type="submit">Save</button></div>
  </form>
</dialog>
<div id="pin-drag-hint" aria-hidden="true" hidden>Drop in pinned excerpts</div>
""";
    internal static MarkdownDocument ParseDocument(string source) => Markdown.Parse(source, Pipeline);
    // Enable document formatting without allowing embedded HTML or arbitrary attributes.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UsePreciseSourceLocation()
        .DisableHtml()
        .Build();

    public static HashSet<int> GetTaskMarkerPositions(string markdown) => Markdown.Parse(markdown, Pipeline)
        .Descendants<TaskList>().Select(task => task.Span.Start)
        .Where(position => IsTaskMarker(markdown, position)).ToHashSet();

    private static bool IsTaskMarker(string markdown, int position) => position >= 0
        && position + 2 < markdown.Length && markdown[position] == '[' && markdown[position + 2] == ']'
        && markdown[position + 1] is ' ' or 'x' or 'X';

    private sealed class TaskRenderer(string markdown, bool editable) : HtmlObjectRenderer<TaskList>
    {
        protected override void Write(HtmlRenderer renderer, TaskList task)
        {
            var position = task.Span.Start;
            renderer.Write("<input type=\"checkbox\" class=\"markdown-task\"");
            if (task.Checked) renderer.Write(" checked");
            if (!editable || !IsTaskMarker(markdown, position)) renderer.Write(" disabled");
            renderer.Write(" data-position=\"").Write(position.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Write("\" aria-label=\"Toggle task\" />");
        }
    }

    public static string CreateDisplayDocument(string markdown, string documentId, bool editableTasks = false,
        bool includeSourceMap = false, MarkdownViewportAnchor? anchor = null, MarkdownPinPresentation? pinState = null)
    {
        using var writer = new System.IO.StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Insert(0, new TaskRenderer(markdown, editableTasks));
        var document = Markdown.Parse(markdown, Pipeline);
        if (includeSourceMap)
        {
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < markdown.Length; i++)
            {
                if (markdown[i] == '\r')
                {
                    if (i + 1 < markdown.Length && markdown[i + 1] == '\n') i++;
                    lineStarts.Add(i + 1);
                }
                else if (markdown[i] == '\n') lineStarts.Add(i + 1);
            }
            foreach (var block in document.Descendants<Block>())
            {
                if (block.Span.Start < 0 || block.Span.End < block.Span.Start) continue;
                var end = lineStarts.BinarySearch(Math.Min(block.Span.End, markdown.Length));
                var endLine = end >= 0 ? end + 1 : ~end;
                var attributes = block.GetAttributes();
                attributes.AddProperty("data-source-start", (block.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                attributes.AddProperty("data-source-end", endLine.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        var runs = includeSourceMap ? MarkdownSelectionMap.Attach(renderer, document, markdown) : [];
        renderer.Render(document);
        var body = writer.ToString();
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
.markdown-task:not(:disabled) { cursor: pointer; }
#task-status { position: sticky; top: 0; background: white; color: #475569; }
#task-status:empty { display: none; }
{{(includeSourceMap ? PinStyles : "")}}
</style>
</head>
<body class="{{(includeSourceMap ? "has-pins" : "")}}">
<div id="task-status" role="status" aria-live="polite"></div>
{{(includeSourceMap ? PinMarkup : "")}}
<main id="markdown-main">
{{body}}
</main>
<script nonce="{{nonce}}">
const documentId = {{documentIdJson}};
const initialAnchor = {{System.Text.Json.JsonSerializer.Serialize(anchor)}};
const sourceRuns = {{System.Text.Json.JsonSerializer.Serialize(runs, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })}};
sourceRuns.forEach(run => {
    let segment = 0;
    run.boundaries = Array.from({length:run.length+1}, (_, i) => {
        while (segment+2 < run.segments.length && run.segments[segment+2] <= i) segment += 2;
        return run.segments[segment+1] + i-run.segments[segment];
    });
});
const buttons = new Map();
let nextCopyId = 0;
const tasksEditable = {{(editableTasks ? "true" : "false")}};
const tasks = () => Array.from(document.querySelectorAll('input.markdown-task'));
const taskStatus = document.getElementById('task-status');
let pendingTask = null;
tasks().forEach(task => {
    task.setAttribute('aria-label', 'Toggle task: ' + task.parentElement.textContent.trim());
});
document.addEventListener('change', e => {
        const task = e.target.closest('input.markdown-task');
        if (!task || !tasksEditable || pendingTask) return;
        pendingTask = { task, previous: !task.checked };
        tasks().forEach(input => input.disabled = true);
        taskStatus.textContent = 'Saving task…';
        window.chrome.webview.postMessage({ action: 'toggleTask', documentId,
            position: Number(task.dataset.position), isChecked: task.checked, pinId:task.closest('[data-pin-id]')?.dataset.pinId });
});
function initializeCodeCopies(root) { root.querySelectorAll('pre > code').forEach(code => {
    const id = nextCopyId++;
    const pre = code.parentElement;
    const wrapper = pre.parentElement.classList.contains('code-block') ? pre.parentElement : document.createElement('div');
    wrapper.className = 'code-block';
    const mapped = pre.matches('[data-source-start]') ? pre : pre.querySelector('[data-source-start]');
    if (mapped) {
        wrapper.dataset.sourceStart = mapped.dataset.sourceStart;
        wrapper.dataset.sourceEnd = mapped.dataset.sourceEnd;
        mapped.removeAttribute('data-source-start');
        mapped.removeAttribute('data-source-end');
    }
    if (pre.parentElement !== wrapper) { pre.replaceWith(wrapper); wrapper.append(pre); }
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'copy-code';
    button.dataset.copyId = id;
    button.textContent = 'Copy';
    button.setAttribute('aria-label', 'Copy code to clipboard');
    button.setAttribute('aria-live', 'polite');
    button.addEventListener('click', () => {
        button.disabled = true;
        window.chrome.webview.postMessage({ action: 'copy', documentId, id, text: code.textContent });
    });
    wrapper.append(button);
    buttons.set(id, button);
}); }
initializeCodeCopies(document.getElementById('markdown-main'));
// Anchor to a source range and its position in the viewport, never document scroll percentage.
function sourceBlocks() {
    return Array.from(document.getElementById('markdown-main').querySelectorAll('[data-source-start]'))
        .filter(e => e.getBoundingClientRect().height > 0 && !e.querySelector('[data-source-start]'));
}
function captureMarkdownAnchor() {
    const blocks = sourceBlocks(), target = innerHeight * .2;
    if (!blocks.length || scrollY <= 1) return { line: 1, viewport: 0, edge: 'start' };
    const element = blocks.reduce((best, e) => {
        const distance = r => Math.max(r.top - target, target - r.bottom, 0);
        return !best || distance(e.getBoundingClientRect()) < distance(best.getBoundingClientRect()) ? e : best;
    }, null);
    const r = element.getBoundingClientRect();
    const y = Math.max(r.top, Math.min(target, r.bottom));
    const start = Number(element.dataset.sourceStart), end = Number(element.dataset.sourceEnd);
    return { line: start + Math.min(.999999, (y-r.top)/r.height) * (end-start+1),
        viewport: y/innerHeight, edge: scrollY + innerHeight >= document.documentElement.scrollHeight - 1 ? 'end' : null };
}
function restoreMarkdownAnchor(anchor) {
    if (!anchor) return;
    if (anchor.edge === 'start') { scrollTo(0, 0); return; }
    if (anchor.edge === 'end') { scrollTo(0, document.documentElement.scrollHeight); return; }
    const blocks = sourceBlocks();
    const distance = e => Math.max(Number(e.dataset.sourceStart)-anchor.line, anchor.line-(Number(e.dataset.sourceEnd)+1), 0);
    const element = blocks.reduce((best, e) => !best || distance(e) < distance(best) ? e : best, null);
    if (!element) return;
    const start = Number(element.dataset.sourceStart), end = Number(element.dataset.sourceEnd);
    const fraction = Math.max(0, Math.min(1, (anchor.line-start)/(end-start+1)));
    const r = element.getBoundingClientRect();
    scrollTo(0, scrollY+r.top+r.height*fraction-anchor.viewport*innerHeight);
}
requestAnimationFrame(() => restoreMarkdownAnchor(initialAnchor));
const pinConfig = {{System.Text.Json.JsonSerializer.Serialize(pinState ?? new MarkdownPinPresentation([], editableTasks), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })}};
{{(includeSourceMap ? PinScript : "")}}
window.chrome.webview.addEventListener('message', ({ data }) => {
    if (data.documentId !== documentId) return;
    if (data.action === 'pins') { if (typeof updateMarkdownPins === 'function') updateMarkdownPins(data.pins, data.error, data.created, data.width, data.wrap); return; }
    if (data.action === 'taskSync') {
        tasks().filter(t => Number(t.dataset.position) === data.position).forEach(t => t.checked = data.isChecked);
        return;
    }
    if (data.action === 'toggleTask') {
        if (!pendingTask) return;
        if (!data.success) pendingTask.task.checked = pendingTask.previous;
        tasks().forEach(input => input.disabled = !tasksEditable);
        pendingTask = null;
        taskStatus.textContent = data.success ? 'Task saved.' : (data.error || 'Could not save task.');
        return;
    }
    const button = buttons.get(data.id);
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
