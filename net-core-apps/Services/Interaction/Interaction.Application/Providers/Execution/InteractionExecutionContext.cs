using IdentityProvider;
using Interaction.Application.Dtos;
using Interaction.Application.Options;
using Interaction.Application.Providers.Cache;
using Interaction.Application.Providers.Events;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Interaction.Domain.Repositories;
using Microsoft.Extensions.Options;
using ContractReactionKind = Interaction.Contracts.ReactionKind;
using DomainReactionKind = Interaction.Domain.AggregatesModel.ReactionKind;

namespace Interaction.Application.Providers.Execution;

/// <summary>
/// Shared execution primitives for Interaction command/query handlers. Domain
/// behavior remains in aggregate roots and feature handlers; this type only
/// centralizes transaction-adjacent mapping, cache keys, and resource guards.
/// </summary>
public sealed class InteractionExecutionContext(
    IInteractionRepository repository,
    IInteractionEventPublisher publisher,
    IEngagementSummaryCache summaryCache,
    TimeProvider timeProvider,
    IOptions<InteractionOptions> options)
{
    internal IInteractionRepository Repository { get; } = repository;
    internal IInteractionEventPublisher Publisher { get; } = publisher;
    internal IEngagementSummaryCache SummaryCache { get; } = summaryCache;
    internal InteractionOptions Options { get; } = options.Value;

    internal DateTimeOffset UtcNow() => timeProvider.GetUtcNow();

    internal static long RequireActor(IIdentityService identity)
    {
        var actor = identity.User.Id;
        if (actor <= 0)
            throw new InteractionUnauthorizedException("A valid authenticated subject is required.");
        return actor;
    }

    internal async Task EnsureActorAvailableAsync(
        long actorId,
        CancellationToken cancellationToken)
    {
        if (await Repository.IsUserErasedAsync(actorId, cancellationToken))
            throw new InteractionUnauthorizedException("The interaction subject is unavailable.");
    }

    internal async Task<ResourceState> RequireAvailableResourceAsync(
        ResourceRef resource,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var state = await Repository.GetResourceStateAsync(resource, forUpdate, cancellationToken);
        EnsurePublic(state, resource);
        return state!;
    }

    internal static void EnsurePublic(ResourceState? state, ResourceRef resource)
    {
        if (state?.IsPubliclyInteractable != true)
            throw new InteractionNotFoundException($"Resource '{resource}' was not found.");
    }

    internal async Task<EngagementSummary> GetOrCreateSummaryAsync(
        ResourceRef resource,
        CancellationToken cancellationToken)
    {
        var summary = await Repository.GetSummaryAsync(resource, true, cancellationToken);
        if (summary is not null)
            return summary;
        summary = new EngagementSummary(resource, UtcNow());
        Repository.AddSummary(summary);
        return summary;
    }

    internal ValueTask TryInvalidateAsync(
        ResourceRef resource,
        long oldVersion,
        CancellationToken cancellationToken) =>
        SummaryCache.InvalidateAsync(resource, oldVersion, cancellationToken);

    internal static EngagementSummaryDto ToDto(ResourceRef resource, EngagementSummary? summary) => new(
        ResourceTypeName(resource.Type), resource.Id,
        summary?.LikeCount ?? 0, summary?.DislikeCount ?? 0, summary?.RatingCount ?? 0,
        summary is { RatingCount: > 0 } ? (double)summary.RatingTotal / summary.RatingCount : null,
        summary?.ViewCount ?? 0,
        summary?.ContributionHasGap == true ? 0 : summary?.CommentCount ?? 0,
        summary?.ContributionHasGap == true ? 0 : summary?.ShareCount ?? 0,
        summary?.Version ?? 0);

    internal static EngagementSummaryChanged ToEvent(EngagementSummary summary) => new(
        Guid.NewGuid(), ResourceTypeName(summary.ResourceType), summary.ResourceId,
        summary.LikeCount, summary.DislikeCount, summary.RatingCount, summary.RatingTotal,
        summary.ViewCount,
        summary.ContributionHasGap ? 0 : summary.CommentCount,
        summary.ContributionHasGap ? 0 : summary.ShareCount,
        summary.Version, summary.UpdatedAtUtc);

    internal static string ResourceTypeName(ResourceType type) => type.ToString().ToLowerInvariant();

    internal static ContractReactionKind Map(DomainReactionKind kind) => kind switch
    {
        DomainReactionKind.Like => ContractReactionKind.Like,
        DomainReactionKind.Dislike => ContractReactionKind.Dislike,
        _ => throw new InteractionValidationException("Reaction kind is invalid."),
    };

}
