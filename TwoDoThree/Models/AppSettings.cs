namespace TwoDoThree.Models;

public sealed class AppSettings
{
    public EmailSettings Email { get; } = new();
}
