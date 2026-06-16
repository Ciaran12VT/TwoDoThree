using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface IGraphMailClient
{
    Task<IReadOnlyList<EmailMessage>> GetLatestInboxMessagesAsync(
        string accessToken,
        int maxMessages,
        CancellationToken cancellationToken);
}
