using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class GraphAuthService : IGraphAuthService
{
    public static readonly string[] Scopes = ["User.Read", "Mail.Read"];

    public async Task<AuthenticationResult?> TryAcquireTokenSilentAsync(
        EmailSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return null;
        }

        var application = await CreateApplicationAsync(settings).ConfigureAwait(false);
        var account = await FindAccountAsync(application, settings).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        try
        {
            return await application
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
    }

    public async Task<AuthenticationResult> AcquireTokenInteractiveAsync(
        EmailSettings settings,
        Window owner,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("A Microsoft Entra client ID is required before signing in.");
        }

        var application = await CreateApplicationAsync(settings).ConfigureAwait(false);
        var builder = application
            .AcquireTokenInteractive(Scopes)
            .WithParentActivityOrWindow(new WindowInteropHelper(owner).Handle);

        if (!string.IsNullOrWhiteSpace(settings.AccountAddress))
        {
            builder = builder.WithLoginHint(settings.AccountAddress.Trim());
        }

        return await builder
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SignOutAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            await ClearCacheAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var application = await CreateApplicationAsync(settings).ConfigureAwait(false);
        foreach (var account in await application.GetAccountsAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await application.RemoveAsync(account).ConfigureAwait(false);
        }
    }

    public Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cachePath = Path.Combine(AppStoragePaths.RootDirectory, AppStoragePaths.MsalCacheFileName);
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        return Task.CompletedTask;
    }

    private static async Task<IPublicClientApplication> CreateApplicationAsync(EmailSettings settings)
    {
        AppStoragePaths.EnsureRootDirectory();

        var authoritySegment = string.IsNullOrWhiteSpace(settings.TenantId)
            ? "organizations"
            : settings.TenantId.Trim();

        var builder = PublicClientApplicationBuilder
            .Create(settings.ClientId.Trim())
            .WithAuthority($"https://login.microsoftonline.com/{authoritySegment}")
            .WithRedirectUri("http://localhost");

        if (settings.UseWindowsAuthentication)
        {
            builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
            {
                ListOperatingSystemAccounts = true,
                Title = "2do3"
            });
        }

        var application = builder.Build();
        var storageProperties = new StorageCreationPropertiesBuilder(
                AppStoragePaths.MsalCacheFileName,
                AppStoragePaths.RootDirectory)
            .Build();
        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
        cacheHelper.RegisterCache(application.UserTokenCache);

        return application;
    }

    private static async Task<IAccount?> FindAccountAsync(IPublicClientApplication application, EmailSettings settings)
    {
        var accounts = await application.GetAccountsAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(settings.AccountAddress))
        {
            return accounts.FirstOrDefault(account =>
                string.Equals(account.Username, settings.AccountAddress.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return accounts.FirstOrDefault();
    }
}
