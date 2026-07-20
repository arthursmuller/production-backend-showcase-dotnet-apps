using Interaction.Application.Dtos;
using Interaction.Application.Providers.Execution;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Features.Engagement.Queries;

public sealed record GetEngagementQuery(InteractionExecutionContext Context)
    : Query<ResourceRef, EngagementSummaryDto>
{
    public override async Task<EngagementSummaryDto> Handle(
        ResourceRef resource,
        CancellationToken cancellationToken)
    {
        var descriptor = await Context.Repository.GetPublicSummaryStateAsync(
            resource, cancellationToken);
        InteractionExecutionContext.EnsurePublic(descriptor.State, resource);
        var version = descriptor.Summary?.Version ?? 0;
        return await Context.SummaryCache.GetOrCreateAsync(
            resource,
            version,
            async token =>
            {
                var current = await Context.Repository.GetPublicSummaryStateAsync(resource, token);
                InteractionExecutionContext.EnsurePublic(current.State, resource);
                return InteractionExecutionContext.ToDto(resource, current.Summary);
            },
            cancellationToken);
    }
}
