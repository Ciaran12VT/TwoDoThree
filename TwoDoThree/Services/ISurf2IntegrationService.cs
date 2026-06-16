using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface ISurf2IntegrationService
{
    Task<string> TestConnectionAsync(Surf2IntegrationSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Surf2ScopeOption>> LoadScopesAsync(
        Surf2IntegrationSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Surf2ResourceCandidate>> LoadResourcesAsync(
        Surf2IntegrationSettings settings,
        string scopeId,
        CancellationToken cancellationToken = default);
}
