using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface ISurf2Launcher
{
    Task OpenResourceAsync(
        Surf2IntegrationSettings settings,
        SurfResourceLink resourceLink,
        CancellationToken cancellationToken = default);
}
