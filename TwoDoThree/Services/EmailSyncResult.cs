using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class EmailSyncResult
{
    public EmailSyncResult(IReadOnlyList<EmailMessage> messages, bool succeeded, string statusMessage)
    {
        Messages = messages;
        Succeeded = succeeded;
        StatusMessage = statusMessage;
    }

    public IReadOnlyList<EmailMessage> Messages { get; }

    public bool Succeeded { get; }

    public string StatusMessage { get; }
}
