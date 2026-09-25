using System.Security.Cryptography;
using System.Text;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public static class MarkdownPinAnchors
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static MarkdownPin Capture(MarkdownPin pin, string text)
    {
        int start = (int)Math.Floor(pin.Start), end = (int)Math.Ceiling(pin.End);
        if (start < 0 || end > text.Length || end <= start) return pin with { Unavailable = "Passage no longer exists." };
        return pin with { Quote = text[start..end], Before = text[Math.Max(0, start - 48)..start],
            After = text[end..Math.Min(text.Length, end + 48)], Unavailable = null };
    }

    // Exact edits from the inline editor preserve identity even when identical text occurs elsewhere.
    public static MarkdownPin ApplyEdit(MarkdownPin pin, int offset, int removed, int inserted, string newText)
    {
        if (pin.Unavailable is not null) return pin;
        if (removed > 0 && offset <= pin.Start && offset + removed >= pin.End)
        {
            if (inserted > 0 && offset == pin.Start && offset + removed == pin.End)
                return Capture(pin with { Start = offset, End = offset + inserted }, newText);
            return pin with { Unavailable = "The pinned passage was deleted. Unpin it or restore the original passage." };
        }
        double Map(double position, bool end) => position < offset || (end && position == offset) ? position
            : position >= offset + removed ? position + inserted - removed : offset + (end ? inserted : 0);
        return Capture(pin with { Start = Map(pin.Start, false), End = Map(pin.End, true) }, newText);
    }

    public static MarkdownPin Resolve(MarkdownPin pin, string? text, bool identicalSource)
    {
        if (text is null) return pin with { Unavailable = pin.Unavailable ?? "Source file is missing or inaccessible." };
        if (pin.Unavailable?.StartsWith("The pinned passage was deleted", StringComparison.Ordinal) == true) return pin;
        // Metadata-only actions may save an unresolved pin alongside the current
        // source hash. That hash does not make its stale offsets trustworthy.
        if (identicalSource && (pin.Unavailable is null || pin.Unavailable.StartsWith("Source file is missing", StringComparison.Ordinal))) return Capture(pin, text);
        if (pin.Quote.Length == 0) return pin with { Unavailable = "Passage cannot be located." };
        var candidates = new List<int>();
        for (int at = 0; at <= text.Length - pin.Quote.Length;)
        {
            at = text.IndexOf(pin.Quote, at, StringComparison.Ordinal);
            if (at < 0) break;
            candidates.Add(at++);
        }
        var contextual = candidates.Where(at =>
            text[Math.Max(0, at - pin.Before.Length)..at] == pin.Before &&
            text[(at + pin.Quote.Length)..Math.Min(text.Length, at + pin.Quote.Length + pin.After.Length)] == pin.After).ToList();
        // A duplicate is never resolved merely by proximity to the old location.
        var matches = contextual.Count == 1 ? contextual : candidates;
        if (matches.Count != 1) return pin with { Unavailable = matches.Count == 0 ? "Passage was changed or deleted." : "Passage is ambiguous after external edits." };
        var delta = matches[0] - Math.Floor(pin.Start);
        return Capture(pin with { Start = pin.Start + delta, End = pin.End + delta }, text);
    }
}
