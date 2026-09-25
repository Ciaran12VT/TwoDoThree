using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class MarkdownPinStore(string? directory = null)
{
    private readonly string directory = directory ?? AppStoragePaths.MarkdownPinsDirectory;
    public static string CanonicalPath(string path) => Path.GetFullPath(path);
    private string MetadataPath(string path) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalPath(path).ToUpperInvariant()))) + ".json");

    public MarkdownPinMetadata Load(string path)
    {
        var file = MetadataPath(path);
        if (!File.Exists(file)) return new() { DocumentPath = CanonicalPath(path) };
        if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new IOException("Pin metadata is too large to read.");
        var metadata = JsonSerializer.Deserialize<MarkdownPinMetadata>(File.ReadAllText(file))
            ?? throw new IOException("Pin metadata is empty.");
        if (metadata.Version != 1 || !string.Equals(metadata.DocumentPath, CanonicalPath(path), StringComparison.OrdinalIgnoreCase)
            || metadata.Pins.Count > 100 || metadata.Pins.Select(p => p.Id).Distinct().Count() != metadata.Pins.Count
            || metadata.Pins.Any(p => !Guid.TryParse(p.Id, out _) || !double.IsFinite(p.Start) || !double.IsFinite(p.End) || p.Start < 0 || p.End <= p.Start))
            throw new IOException("Pin metadata has an unsupported format. The existing metadata has been preserved.");
        return metadata;
    }

    public void Save(MarkdownPinMetadata metadata)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
        if (bytes.Length > 16 * 1024 * 1024) throw new IOException("Pin metadata would exceed 16 MB. Unpin an excerpt before adding more.");
        Directory.CreateDirectory(directory);
        var path = MetadataPath(metadata.DocumentPath);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
