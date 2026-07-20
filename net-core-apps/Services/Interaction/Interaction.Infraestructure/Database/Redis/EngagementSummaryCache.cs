using Inspacy.Caching;
using Interaction.Application.Dtos;
using Interaction.Application.Providers.Cache;
using Interaction.Domain.AggregatesModel;
using Microsoft.Extensions.Logging;

namespace Interaction.Infraestructure.Database.Redis;

internal sealed class EngagementSummaryCache(
    ISharedCache cache,
    ICacheKeyFactory cacheKeys,
    ILogger<EngagementSummaryCache> logger)
    : IEngagementSummaryCache
{
    private static readonly CacheEntryPolicy SummaryPolicy = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5));

    public ValueTask<EngagementSummaryDto> GetOrCreateAsync(
        ResourceRef resource,
        long version,
        Func<CancellationToken, ValueTask<EngagementSummaryDto>> authoritativeSource,
        CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(
            CreateKey(resource, version),
            authoritativeSource,
            SummaryPolicy,
            cancellationToken);

    public async ValueTask InvalidateAsync(
        ResourceRef resource,
        long version,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(CreateKey(resource, version), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Summary cache invalidation failed for {ResourceType}; entity-specific keys preserve correctness.",
                resource.Type);
        }
    }

    private CacheKey CreateKey(ResourceRef resource, long version) =>
        cacheKeys.Create("engagement-summary", resource.ToString(), version);
}
