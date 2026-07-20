using IdentityProvider;
using Interaction.Application.Dtos;
using Interaction.Application.Options;
using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;

namespace Interaction.Application.Features.InteractionStatus.Queries;

public sealed record GetViewerStatusesQuery(
    InteractionExecutionContext Context,
    IIdentityService Identity) : Query<IReadOnlyList<ResourceRef>, IReadOnlyList<ViewerInteractionStatusDto>>
{
    public override async Task<IReadOnlyList<ViewerInteractionStatusDto>> Handle(
        IReadOnlyList<ResourceRef> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        InteractionInputGuard.ValidateStatusBatchCount(
            resources.Count,
            Context.Options.StatusBatchMaximum);
        InteractionTelemetry.StatusBatchSize.Record(resources.Count);
        if (resources.Distinct().Count() != resources.Count)
            throw new InteractionValidationException("Status batch cannot contain duplicate resources.");

        var states = await Context.Repository.GetViewerStatusesAsync(
            InteractionExecutionContext.RequireActor(Identity), resources, cancellationToken);
        return states.Select(state => new ViewerInteractionStatusDto(
            InteractionExecutionContext.ResourceTypeName(state.Resource.Type),
            state.Resource.Id,
            state.IsAvailable,
            state.Reaction?.ToString().ToLowerInvariant(),
            state.Rating,
            state.ReactionVersion,
            state.RatingVersion)).ToArray();
    }
}
