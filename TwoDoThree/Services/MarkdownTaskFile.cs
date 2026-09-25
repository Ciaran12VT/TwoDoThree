using System.IO;
using System.Text;

namespace TwoDoThree.Services;

/// <summary>A file snapshot supporting source edits and precise task-marker updates.</summary>
public sealed class MarkdownTaskFile
{
    private readonly string path;
    private readonly Encoding encoding;
    private readonly int preambleLength;
    private readonly HashSet<int> taskPositions;
    private readonly object gate = new();
    private byte[] snapshot;
    public string Text { get; private set; }

    private MarkdownTaskFile(string path, byte[] bytes, Encoding encoding, int preambleLength)
    {
        this.path = path;
        this.encoding = encoding;
        this.preambleLength = preambleLength;
        snapshot = bytes;
        Text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        taskPositions = MarkdownPreviewHtml.GetTaskMarkerPositions(Text);
    }

    // Large or unsupported documents still use the existing read-only preview.
    public static MarkdownTaskFile? Load(string path, int maximumBytes)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) return null;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var span = bytes.AsSpan();
        Encoding encoding = new UTF8Encoding(false, true);
        int preamble = 0;
        if (span.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })
            || span.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) return null;
        if (span.StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); preamble = 2; }
        else if (span.StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); preamble = 2; }
        else if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) preamble = 3;
        try
        {
            var file = new MarkdownTaskFile(path, bytes, encoding, preamble);
            return file.Text.Contains('\0') ? null : file;
        }
        catch (DecoderFallbackException) { return null; }
    }

    public void SetChecked(int position, bool isChecked)
    {
        lock (gate)
        {
            if (!taskPositions.Contains(position)) throw new ArgumentException("This is not a Markdown task checkbox.");
            // Deny concurrent writers and compare the entire snapshot before changing a byte.
            // Never rewrite from a stale preview or truncate a large source file.
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length != snapshot.Length) throw ChangedFile();
            var current = new byte[snapshot.Length];
            stream.ReadExactly(current);
            if (!current.AsSpan().SequenceEqual(snapshot)) throw ChangedFile();
            if ((Text[position + 1] is 'x' or 'X') == isChecked) return;

            var marker = isChecked ? "x" : " ";
            var offset = preambleLength + encoding.GetByteCount(Text.AsSpan(0, position + 1));
            var replacement = encoding.GetBytes(marker);
            stream.Position = offset;
            stream.Write(replacement);
            stream.Flush(flushToDisk: true);
            replacement.CopyTo(snapshot, offset);
            Text = Text[..(position + 1)] + marker + Text[(position + 2)..];
        }
    }

    public void SaveText(string text, int maximumBytes)
    {
        lock (gate)
        {
            var body = encoding.GetBytes(text);
            if (body.Length + preambleLength > maximumBytes)
                throw new IOException("The edited file exceeds the 1 MB viewer limit. Shorten it or use an external editor.");
            if (text.Contains('\0')) throw new ArgumentException("Markdown cannot contain null characters.");
            byte[] replacement = [.. snapshot.AsSpan(0, preambleLength), .. body];
            var positions = MarkdownPreviewHtml.GetTaskMarkerPositions(text);
            // Stage the entire replacement beside the original. Failed writes leave it untouched.
            var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".2do3-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var staged = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    staged.Write(replacement);
                    staged.Flush(flushToDisk: true);
                }
                // Keep other writers out while checking and replacing. Delete sharing allows the
                // atomic replacement and preserves the destination's permissions on Windows.
                using var original = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (original.Length != snapshot.Length) throw ChangedFile();
                var current = new byte[snapshot.Length];
                original.ReadExactly(current);
                if (!current.AsSpan().SequenceEqual(snapshot)) throw ChangedFile();
                if (replacement.AsSpan().SequenceEqual(snapshot)) return;
                File.Replace(temporary, path, null);
                snapshot = replacement;
                Text = text;
                taskPositions.Clear();
                taskPositions.UnionWith(positions);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static IOException ChangedFile() => new("The file changed outside this preview. Your changes have not been written. Copy any edits you want to keep, then reopen the file.");
}
