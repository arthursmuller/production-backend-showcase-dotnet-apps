using Interaction.Application.Dtos;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Providers.Cache;

/// <summary>
/// Application-owned cache boundary for the public engagement projection.
/// Infrastructure owns cache keys, expiry policy, and the cache provider.
/// </summary>
public interface IEngagementSummaryCache
{
    ValueTask<EngagementSummaryDto> GetOrCreateAsync(
        ResourceRef resource,
        long version,
        Func<CancellationToken, ValueTask<EngagementSummaryDto>> authoritativeSource,
        CancellationToken cancellationToken = default);

    ValueTask InvalidateAsync(
        ResourceRef resource,
        long version,
        CancellationToken cancellationToken = default);
}
