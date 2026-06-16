using System.Windows;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface IEmailProvider
{
    IReadOnlyList<EmailMessage> LoadCachedMessages();

    Task<EmailSyncResult> RefreshInboxAsync(
        EmailSettings settings,
        bool allowInteractiveSignIn,
        Window? owner,
        CancellationToken cancellationToken);
}
