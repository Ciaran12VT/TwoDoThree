using System.Collections.ObjectModel;
using System.IO;

namespace TwoDoThree.Models;

/// <summary>A transient child of a linked folder. Only root ResourceItems are saved.</summary>
public sealed class FileSystemResourceNode
{
    public FileSystemResourceNode(string path, bool isFolder, string? message = null, int? nextOffset = null)
    {
        Path = path;
        IsFolder = isFolder;
        Message = message;
        NextOffset = nextOffset;
        Name = message ?? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        if (string.IsNullOrWhiteSpace(Name)) Name = path;
        if (isFolder && message is null) Children.Add(CreatePlaceholder());
    }

    public static FileSystemResourceNode CreatePlaceholder() => new(string.Empty, false, "Loading…");

    public string Path { get; }
    public bool IsFolder { get; }
    public string? Message { get; }
    public int? NextOffset { get; }
    public ObservableCollection<FileSystemResourceNode>? Siblings { get; set; }
    public string Name { get; }
    public bool IsLoaded { get; set; }
    public ObservableCollection<FileSystemResourceNode> Children { get; } = new();

    public string Icon => Message is not null ? "…" : IsFolder ? "📁" :
        System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
        {
            ".pdf" => "PDF",
            ".doc" or ".docx" => "W",
            _ => "📄"
        };

    public string IconColor => System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
    {
        ".pdf" => "#B91C1C",
        ".doc" or ".docx" => "#1D4ED8",
        _ => "#64748B"
    };
}
