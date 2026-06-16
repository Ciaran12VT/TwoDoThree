using System.Net.Http;
using System.Windows;
using Microsoft.Identity.Client;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class GraphEmailProvider : IEmailProvider
{
    private readonly IGraphAuthService authService;
    private readonly IGraphMailClient mailClient;
    private readonly IEmailCacheStore cacheStore;

    public GraphEmailProvider(
        IGraphAuthService authService,
        IGraphMailClient mailClient,
        IEmailCacheStore cacheStore)
    {
        this.authService = authService;
        this.mailClient = mailClient;
        this.cacheStore = cacheStore;
    }

    public IReadOnlyList<EmailMessage> LoadCachedMessages()
    {
        return cacheStore.Load();
    }

    public async Task<EmailSyncResult> RefreshInboxAsync(
        EmailSettings settings,
        bool allowInteractiveSignIn,
        Window? owner,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return new EmailSyncResult(cacheStore.Load(), false, "Configure a Microsoft Entra client ID to sync Outlook.");
        }

        try
        {
            var result = await authService.TryAcquireTokenSilentAsync(settings, cancellationToken).ConfigureAwait(false);
            if (result is null && allowInteractiveSignIn)
            {
                if (owner is null)
                {
                    return new EmailSyncResult(cacheStore.Load(), false, "A window is required for interactive sign-in.");
                }

                result = await authService.AcquireTokenInteractiveAsync(settings, owner, cancellationToken).ConfigureAwait(false);
            }

            if (result is null)
            {
                return new EmailSyncResult(cacheStore.Load(), false, "Sign in required to sync Outlook.");
            }

            var messages = await mailClient
                .GetLatestInboxMessagesAsync(result.AccessToken, settings.MaxInboxMessages, cancellationToken)
                .ConfigureAwait(false);
            cacheStore.Save(messages);
            return new EmailSyncResult(messages, true, $"Synced {messages.Count} Inbox emails at {DateTime.Now:t}.");
        }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
        {
            return new EmailSyncResult(cacheStore.Load(), false, "Sign-in cancelled.");
        }
        catch (MsalException ex)
        {
            return new EmailSyncResult(cacheStore.Load(), false, $"Outlook sign-in failed: {ex.Message}");
        }
        catch (GraphMailException ex)
        {
            return new EmailSyncResult(cacheStore.Load(), false, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new EmailSyncResult(cacheStore.Load(), false, $"Outlook sync failed: {ex.Message}");
        }
    }
}
