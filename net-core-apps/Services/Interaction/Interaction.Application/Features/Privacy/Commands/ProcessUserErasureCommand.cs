using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Repositories;
using MessageBrokerContracts.Privacy.Restricted;

namespace Interaction.Application.Features.Privacy.Commands;

public sealed record ProcessUserErasureCommand(
    InteractionExecutionContext Context,
    IInteractionErasureRepository Erasures)
    : Command<string, int>
{
    public override async Task<int> Handle(
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var store = Context.Repository;
        var now = Context.UtcNow();
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        var erasure = await Erasures.LockNextPendingAsync(now, cancellationToken);
        if (erasure is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        _ = erasure.Acquire(
            leaseOwner,
            now,
            TimeSpan.FromSeconds(Context.Options.ErasureLeaseSeconds));
        _ = await store.DeleteExpiredViewObservationsAsync(
            now,
            Context.Options.ErasureBatchSize,
            cancellationToken);

        var remaining = Context.Options.ErasureBatchSize;
        var reactions = await Erasures.LockReactionsAsync(
            erasure.UserId,
            remaining,
            cancellationToken);
        remaining -= reactions.Count;
        var ratings = remaining > 0
            ? await Erasures.LockRatingsAsync(erasure.UserId, remaining, cancellationToken)
            : [];
        remaining -= ratings.Count;

        var summaries = new Dictionary<ResourceRef, SummaryChange>();
        foreach (var reaction in reactions)
        {
            var resource = new ResourceRef(reaction.ResourceType, reaction.ResourceId);
            var summary = await GetSummaryAsync(resource, summaries, cancellationToken);
            if (!summary.Summary.IsErased)
                summary.Summary.ApplyReaction(reaction.Kind, null, now);
            store.RemoveReaction(reaction);
            await Context.Publisher.PublishReactionAsync(
                new ReactionChanged(
                    Guid.NewGuid(),
                    InteractionExecutionContext.ResourceTypeName(resource.Type),
                    resource.Id,
                    erasure.UserId,
                    null,
                    reaction.Version + 1,
                    now),
                cancellationToken);
        }

        foreach (var rating in ratings)
        {
            var resource = new ResourceRef(rating.ResourceType, rating.ResourceId);
            var summary = await GetSummaryAsync(resource, summaries, cancellationToken);
            if (!summary.Summary.IsErased)
                summary.Summary.ApplyRating(rating.Score, null, now);
            store.RemoveRating(rating);
            await Context.Publisher.PublishRatingAsync(
                new RatingChanged(
                    Guid.NewGuid(),
                    InteractionExecutionContext.ResourceTypeName(resource.Type),
                    resource.Id,
                    erasure.UserId,
                    null,
                    rating.Version + 1,
                    now),
                cancellationToken);
        }

        foreach (var change in summaries.Values
                     .Where(change => change.Summary.Version != change.OldVersion))
        {
            await Context.Publisher.PublishSummaryAsync(
                InteractionExecutionContext.ToEvent(change.Summary),
                cancellationToken);
        }

        // Flush tracked reaction/rating removals inside the still-open transaction before
        // asking the database whether actor-owned rows remain. Without this flush, a fully
        // drained subject always required an unnecessary extra scheduler pass.
        await store.SaveChangesAsync(cancellationToken);
        var actorDataRemains = await Erasures.HasActorDataAsync(
            erasure.UserId,
            cancellationToken);
        var advancedToAwaiting = !actorDataRemains
            && erasure.Stage == InteractionUserErasureStage.ActorData;
        if (!actorDataRemains)
            erasure.AwaitRawViewExpiry(now);

        var completed = !actorDataRemains && now >= erasure.RawViewsExpireAtUtc;
        if (completed)
        {
            erasure.Complete(now);
            await Context.Publisher.PublishErasureCompletedAsync(
                new UserErasureCompleted
                {
                    EventId = Guid.NewGuid(),
                    RequestId = erasure.RequestId,
                    UserId = erasure.UserId,
                    ErasureVersion = erasure.ErasureVersion,
                    Service = "interaction",
                    Outcome = UserErasureOutcomes.Completed,
                    CompletedAtUtc = now,
                },
                cancellationToken);
        }
        else
        {
            erasure.Release(now);
        }

        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var workUnits = reactions.Count
            + ratings.Count
            + (advancedToAwaiting ? 1 : 0)
            + (completed ? 1 : 0);
        InteractionTelemetry.ErasureBatchWork.Record(workUnits);
        if (completed)
        {
            InteractionTelemetry.ErasureCompletions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "completed"));
        }
        foreach (var (resource, change) in summaries)
        {
            if (change.Summary.Version != change.OldVersion)
                await Context.TryInvalidateAsync(resource, change.OldVersion, cancellationToken);
        }
        return workUnits;
    }

    private async Task<SummaryChange> GetSummaryAsync(
        ResourceRef resource,
        IDictionary<ResourceRef, SummaryChange> summaries,
        CancellationToken cancellationToken)
    {
        if (summaries.TryGetValue(resource, out var existing))
            return existing;
        var summary = await Context.GetOrCreateSummaryAsync(resource, cancellationToken);
        var change = new SummaryChange(summary, summary.Version);
        summaries.Add(resource, change);
        return change;
    }

    private sealed record SummaryChange(EngagementSummary Summary, long OldVersion);
}
