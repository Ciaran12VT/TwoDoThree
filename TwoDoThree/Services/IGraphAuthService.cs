using System.Windows;
using Microsoft.Identity.Client;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface IGraphAuthService
{
    Task<AuthenticationResult?> TryAcquireTokenSilentAsync(EmailSettings settings, CancellationToken cancellationToken);

    Task<AuthenticationResult> AcquireTokenInteractiveAsync(EmailSettings settings, Window owner, CancellationToken cancellationToken);

    Task SignOutAsync(EmailSettings settings, CancellationToken cancellationToken);

    Task ClearCacheAsync(CancellationToken cancellationToken);
}
