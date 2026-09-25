namespace TwoDoThree.Models;

public sealed record MarkdownPin
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public double Start { get; init; }
    public double End { get; init; }
    public string Quote { get; init; } = "";
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string SelectedText { get; init; } = "";
    public int Order { get; init; }
    public string? Unavailable { get; init; }
    public string? Title { get; init; }
    public bool Collapsed { get; init; }
}

public sealed record MarkdownPinMetadata
{
    public int Version { get; init; } = 1;
    public string DocumentPath { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public List<MarkdownPin> Pins { get; init; } = [];
    public double SidebarWidth { get; init; } = 330;
    public bool Wrap { get; init; } = true;
}
