using System.Windows;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class CompositeEmailProvider : IEmailProvider
{
    private readonly IEmailProvider graphProvider;
    private readonly IEmailProvider classicOutlookProvider;
    private readonly IEmailCacheStore cacheStore;

    public CompositeEmailProvider(
        IEmailProvider graphProvider,
        IEmailProvider classicOutlookProvider,
        IEmailCacheStore cacheStore)
    {
        this.graphProvider = graphProvider;
        this.classicOutlookProvider = classicOutlookProvider;
        this.cacheStore = cacheStore;
    }

    public IReadOnlyList<EmailMessage> LoadCachedMessages()
    {
        return cacheStore.Load();
    }

    public Task<EmailSyncResult> RefreshInboxAsync(
        EmailSettings settings,
        bool allowInteractiveSignIn,
        Window? owner,
        CancellationToken cancellationToken)
    {
        return settings.Source switch
        {
            EmailSource.MicrosoftGraph => graphProvider.RefreshInboxAsync(settings, allowInteractiveSignIn, owner, cancellationToken),
            EmailSource.ClassicOutlook => classicOutlookProvider.RefreshInboxAsync(settings, allowInteractiveSignIn, owner, cancellationToken),
            EmailSource.ManualImport => Task.FromResult(new EmailSyncResult(
                cacheStore.Load(),
                true,
                "Manual import selected. Use Import Email to add .eml or .msg files.")),
            _ => Task.FromResult(new EmailSyncResult(cacheStore.Load(), false, "Unknown email source."))
        };
    }
}
