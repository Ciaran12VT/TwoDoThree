namespace TwoDoThree.Models;

public sealed class AppSettings
{
    public EmailSettings Email { get; } = new();

    public TagSettings Tags { get; } = new();

    public DatabaseSettings Database { get; } = new();

    public Surf2IntegrationSettings Surf2 { get; } = new();
}
