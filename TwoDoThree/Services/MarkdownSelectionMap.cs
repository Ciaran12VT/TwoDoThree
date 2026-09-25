using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.TaskLists;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed record MarkdownTextRun(int Id, [property: System.Text.Json.Serialization.JsonIgnore] string Text,
    [property: System.Text.Json.Serialization.JsonIgnore] double[] Boundaries)
{
    public int Length => Text.Length;
    // Most source runs are contiguous. Send only offset discontinuities instead of
    // one JSON number per character, keeping large previews below WebView's HTML limit.
    public double[] Segments => Boundaries.SelectMany((value, index) => index == 0 || Math.Abs(value - Boundaries[index - 1] - 1) > .00001
        ? new double[] { index, value } : []).ToArray();
}

public static class MarkdownSelectionMap
{
    public static List<MarkdownTextRun> Attach(HtmlRenderer renderer, MarkdownDocument document, string source)
    {
        renderer.ObjectRenderers.Insert(0, new MappedHtmlBlockRenderer());
        var mapped = Build(document, source);
        renderer.ObjectWriteBefore += (_, obj) =>
        {
            if (mapped.TryGetValue(obj, out var run))
                renderer.Write(obj is Block ? "<div data-run=\"" : "<span data-run=\"").Write(run.Id.ToString()).Write("\">");
        };
        renderer.ObjectWriteAfter += (_, obj) =>
        {
            if (mapped.ContainsKey(obj)) renderer.Write(obj is Block ? "</div>" : "</span>");
        };
        return mapped.Values.ToList();
    }

    // Embedded HTML remains literal text, consistent with the preview's DisableHtml
    // policy. Rendering it directly avoids unmapped layout separators around the run.
    private sealed class MappedHtmlBlockRenderer : HtmlObjectRenderer<HtmlBlock>
    {
        protected override void Write(HtmlRenderer renderer, HtmlBlock obj) => renderer.WriteEscape(obj.Lines.ToString());
    }

    public static List<MarkdownTextRun> GetRuns(string source) => Build(MarkdownPreviewHtml.ParseDocument(source), source).Values.ToList();

    private static Dictionary<MarkdownObject, MarkdownTextRun> Build(MarkdownDocument document, string source)
    {
        var result = new Dictionary<MarkdownObject, MarkdownTextRun>();
        foreach (var obj in document.Descendants())
        {
            bool imageAlt = false;
            for (var parent = (obj as Inline)?.Parent; parent is not null; parent = parent.Parent)
                if (parent is LinkInline { IsImage: true }) { imageAlt = true; break; }
            if (imageAlt) continue;
            string? text = obj switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                HtmlEntityInline entity => entity.Transcoded.ToString(),
                AutolinkInline link => link.Url,
                HtmlInline html => html.Tag,
                HtmlBlock html => html.Lines.ToString(),
                CodeBlock block => string.Join("\n", block.Lines.Lines.Take(block.Lines.Count).Select(l => l.Slice.ToString())) + "\n",
                _ => null
            };
            if (string.IsNullOrEmpty(text) || obj.Span.Start < 0 || obj.Span.End < obj.Span.Start) continue;
            var start = obj is CodeBlock codeBlock && codeBlock.Lines.Count > 0
                ? codeBlock.Lines.Lines[0].Slice.Start : obj is CodeInline inlineCode ? obj.Span.Start + inlineCode.DelimiterCount : obj.Span.Start;
            var end = Math.Min(source.Length, obj.Span.End + 1);
            // Entities are indivisible source syntax but can contain multiple rendered
            // characters. Fractional source positions keep their partial selections exact.
            var boundaries = new double[text.Length + 1];
            int cursor = start;
            bool aligned = obj is not HtmlEntityInline;
            for (int i = 0; i < text.Length && aligned; i++)
            {
                while (cursor < end && source[cursor] != text[i] && !(text[i] == '\n' && source[cursor] == '\r')) cursor++;
                if (cursor == end)
                {
                    // Renderers add a terminal newline to code blocks.
                    if (i == text.Length - 1 && text[i] == '\n') { boundaries[i] = boundaries[i + 1] = end; break; }
                    aligned = false;
                    break;
                }
                boundaries[i] = cursor;
                cursor++;
                boundaries[i + 1] = cursor;
            }
            if (!aligned)
                for (int i = 0; i <= text.Length; i++) boundaries[i] = start + (double)(end - start) * i / text.Length;
            result.Add(obj, new(result.Count, text, boundaries));
        }
        return result;
    }

    public static void Validate(string source, double start, double end, string text)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end > source.Length || start >= end
            || string.IsNullOrWhiteSpace(text) || text.Length > 100_000) throw new ArgumentException("Select a non-empty passage within this document.");
        var runs = GetRuns(source);
        bool Boundary(double value) => runs.Any(r => r.Boundaries.Any(b => Math.Abs(value - b) < .00001));
        if (!Boundary(start) || !Boundary(end)) throw new ArgumentException("Selection is not mapped to the source document.");
        var pieces = new List<string>();
        foreach (var run in runs)
        {
            var chars = Enumerable.Range(0, run.Text.Length)
                .Where(i => run.Boundaries[i] >= start - .00001 && run.Boundaries[i + 1] <= end + .00001);
            pieces.Add(string.Concat(chars.Select(i => run.Text[i])));
        }
        string Normalize(string value) => Regex.Replace(value, @"\s+", "");
        if (Normalize(string.Concat(pieces)) != Normalize(text)) throw new ArgumentException("Selection no longer matches the rendered passage.");
    }

    public static bool OwnsTask(string source, MarkdownPin pin, int position)
    {
        var item = MarkdownPreviewHtml.ParseDocument(source).Descendants<ListItemBlock>()
            .Where(i => i.Descendants<TaskList>().Any(t => t.Span.Start == position))
            .OrderBy(i => i.Span.Length).FirstOrDefault();
        return item is not null && pin.Start <= item.Span.End && pin.End > item.Span.Start;
    }
}
