namespace TwoDoThree.Models;

public sealed class EmailMessage
{
    public string Id { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public DateTime ReceivedOn { get; init; }

    public string Preview { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string DisplayTitle => $"{From} - {Subject}";
}
