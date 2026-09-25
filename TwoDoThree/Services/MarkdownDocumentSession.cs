using System.IO;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed record MarkdownDocumentChange(string? Text, int? TaskPosition = null, bool? Checked = null);

/// <summary>One serialized file/pin authority per canonical path in this application.</summary>
public sealed class MarkdownDocumentSession
{
    private static readonly Dictionary<string, WeakReference<MarkdownDocumentSession>> Sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly MarkdownPinStore store;
    private MarkdownTaskFile? file;
    private MarkdownPinMetadata metadata;
    private bool metadataReadable = true;
    private bool initialized;
    private const int Limit = 1024 * 1024;
    public string Path { get; }
    public event EventHandler<MarkdownDocumentChange>? Changed;
    public string? Error { get; private set; }

    public static MarkdownDocumentSession Get(string path)
    {
        path = MarkdownPinStore.CanonicalPath(path);
        lock (Sessions)
        {
            if (Sessions.TryGetValue(path, out var weak) && weak.TryGetTarget(out var found)) return found;
            var session = new MarkdownDocumentSession(path, new MarkdownPinStore());
            Sessions[path] = new(session);
            return session;
        }
    }

    public MarkdownDocumentSession(string path, MarkdownPinStore store)
    {
        Path = MarkdownPinStore.CanonicalPath(path);
        this.store = store;
        try { metadata = store.Load(Path); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            metadata = new() { DocumentPath = Path };
            Error = "Pins could not be loaded: " + e.Message;
            metadataReadable = false;
        }
        Refresh();
    }

    public string? Text { get { lock (gate) return file?.Text; } }
    public List<MarkdownPin> Pins { get { lock (gate) return metadata.Pins.ToList(); } }
    public double SidebarWidth { get { lock (gate) return metadata.SidebarWidth; } }
    public bool Wrap { get { lock (gate) return metadata.Wrap; } }

    public void SetViewPreferences(double width, bool wrap)
    {
        if (!double.IsFinite(width) || width < 180 || width > 1600) throw new ArgumentException("Invalid sidebar width.");
        lock (gate)
        {
            if (!metadataReadable) throw new IOException(Error);
            var updated = metadata with { SidebarWidth = width, Wrap = wrap };
            store.Save(updated);
            metadata = updated;
            Error = null;
        }
        Changed?.Invoke(this, new(Text));
    }
    public MarkdownTaskFile? LoadSnapshot() { lock (gate) return file is null ? null : MarkdownTaskFile.Load(Path, Limit); }

    public void Refresh()
    {
        MarkdownDocumentChange? change = null;
        lock (gate)
        {
            MarkdownTaskFile? updated;
            try { updated = MarkdownTaskFile.Load(Path, Limit); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { updated = null; }
            if (initialized && updated?.Text == file?.Text) { file = updated; return; }
            initialized = true;
            var identical = updated is not null && MarkdownPinAnchors.Hash(updated.Text) == metadata.SourceHash;
            metadata = metadata with { Pins = metadata.Pins.Select(p => MarkdownPinAnchors.Resolve(p, updated?.Text, identical)).ToList() };
            file = updated;
            change = new(file?.Text);
        }
        Changed?.Invoke(this, change);
    }

    private void RequireCurrent(string expected, bool allowTaskChanges = false)
    {
        if (file is null || (file.Text != expected && !(allowTaskChanges && OnlyTaskMarkersChanged(expected, file.Text))))
            throw new IOException("The file changed in another window. Your draft has been kept; discard it to reload.");
    }

    internal static bool OnlyTaskMarkersChanged(string oldText, string text)
    {
        if (oldText.Length != text.Length) return false;
        var positions = MarkdownPreviewHtml.GetTaskMarkerPositions(oldText);
        for (int i = 0; i < text.Length; i++)
            if (oldText[i] != text[i] && !(positions.Contains(i - 1) && oldText[i] is ' ' or 'x' or 'X' && text[i] is ' ' or 'x' or 'X')) return false;
        return true;
    }

    private void Persist(List<MarkdownPin> pins)
    {
        if (!metadataReadable) throw new IOException(Error);
        var updated = metadata with { Pins = pins, SourceHash = file is null ? metadata.SourceHash : MarkdownPinAnchors.Hash(file.Text) };
        store.Save(updated);
        metadata = updated;
        Error = null;
    }

    public void AddPin(string expected, double start, double end, string selectedText)
    {
        lock (gate)
        {
            RequireCurrent(expected);
            if (metadata.Pins.Count >= 100) throw new IOException("This document already has 100 pins. Unpin an excerpt first.");
            MarkdownSelectionMap.Validate(expected, start, end, selectedText);
            var pin = MarkdownPinAnchors.Capture(new() { Start = start, End = end, SelectedText = selectedText,
                Order = metadata.Pins.Count == 0 ? 0 : metadata.Pins.Max(p => p.Order) + 1 }, expected);
            Persist([.. metadata.Pins, pin]);
        }
        Changed?.Invoke(this, new(Text));
    }

    public void Unpin(string id)
    {
        lock (gate) Persist(metadata.Pins.Where(p => p.Id != id).ToList());
        Changed?.Invoke(this, new(Text));
    }

    public void RenamePin(string id, string title)
    {
        title = title.Trim();
        if (title.Length is 0 or > 200 || title.Any(char.IsControl))
            throw new ArgumentException("Enter a title of 1 to 200 characters on one line.");
        UpdatePin(id, pin => pin with { Title = title });
    }

    public void SetPinCollapsed(string id, bool collapsed) => UpdatePin(id, pin => pin with { Collapsed = collapsed });

    private void UpdatePin(string id, Func<MarkdownPin, MarkdownPin> update)
    {
        lock (gate)
        {
            if (!metadata.Pins.Any(p => p.Id == id)) throw new IOException("This pin no longer exists.");
            Persist(metadata.Pins.Select(p => p.Id == id ? update(p) : p).ToList());
        }
        Changed?.Invoke(this, new(Text));
    }

    public void ClearPins()
    {
        lock (gate) Persist([]);
        Changed?.Invoke(this, new(Text));
    }

    public void SetChecked(string expected, int position, bool value, string? pinId = null)
    {
        lock (gate)
        {
            RequireCurrent(expected, true);
            if (pinId is not null)
            {
                var pin = metadata.Pins.SingleOrDefault(p => p.Id == pinId && p.Unavailable is null)
                    ?? throw new IOException("This pin is unavailable.");
                if (!MarkdownSelectionMap.OwnsTask(expected, pin, position)) throw new IOException("This checkbox does not belong to the pin.");
            }
            file!.SetChecked(position, value);
            var pins = metadata.Pins.Select(p => MarkdownPinAnchors.ApplyEdit(p, position + 1, 1, 1, file.Text)).ToList();
            // The source write already succeeded. A metadata failure must not report a failed checkbox write.
            try { Persist(pins); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error = "Source saved, but pins could not be saved: " + e.Message; metadata = metadata with { Pins = pins }; }
        }
        Changed?.Invoke(this, new(Text, position, value));
    }

    public void SaveText(string expected, string text, List<MarkdownPin> draftPins)
    {
        lock (gate)
        {
            RequireCurrent(expected);
            file!.SaveText(text, Limit);
            var pins = metadata.Pins.Select(p => (draftPins.SingleOrDefault(d => d.Id == p.Id)
                ?? MarkdownPinAnchors.Resolve(p, text, false)) with { Title = p.Title, Collapsed = p.Collapsed }).ToList();
            try { Persist(pins); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error = "Source saved, but pins could not be saved: " + e.Message; metadata = metadata with { Pins = pins }; }
        }
        Changed?.Invoke(this, new(Text));
    }
}
