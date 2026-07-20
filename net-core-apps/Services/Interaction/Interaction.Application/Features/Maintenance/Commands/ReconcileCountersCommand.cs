using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Features.Maintenance.Commands;

public sealed record ReconcileCountersCommand(InteractionExecutionContext Context)
    : Command<int, int>
{
    public override async Task<int> Handle(int maximum, CancellationToken cancellationToken)
    {
        if (maximum is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        var store = Context.Repository;
        await using var transaction = await store.BeginReadCommittedAsync(cancellationToken);
        var summaries = await store.LockSummariesForReconciliationAsync(
            maximum, cancellationToken);
        var corrected = 0;
        var invalidations = new List<(ResourceRef Resource, long Version)>();
        var drift = new List<(string Counter, long Projected, long Authoritative)>();
        foreach (var summary in summaries)
        {
            var resource = new ResourceRef(summary.ResourceType, summary.ResourceId);
            var reactions = await store.CountReactionsAsync(resource, cancellationToken);
            var ratings = await store.AggregateRatingsAsync(resource, cancellationToken);
            drift.Add(("likes", summary.LikeCount, reactions.Likes));
            drift.Add(("dislikes", summary.DislikeCount, reactions.Dislikes));
            drift.Add(("rating_count", summary.RatingCount, ratings.Count));
            drift.Add(("rating_total", summary.RatingTotal, ratings.Total));
            var oldVersion = summary.Version;
            if (summary.Reconcile(
                    reactions.Likes,
                    reactions.Dislikes,
                    ratings.Count,
                    ratings.Total,
                    Context.UtcNow()))
            {
                corrected++;
                invalidations.Add((resource, oldVersion));
                await Context.Publisher.PublishSummaryAsync(
                    InteractionExecutionContext.ToEvent(summary), cancellationToken);
            }
        }
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var item in drift)
        {
            InteractionTelemetry.RecordReconciliationDrift(
                item.Counter,
                item.Projected,
                item.Authoritative);
        }
        foreach (var item in invalidations)
            await Context.TryInvalidateAsync(item.Resource, item.Version, cancellationToken);
        if (corrected > 0)
            InteractionTelemetry.ReconciledSummaries.Add(corrected);
        return corrected;
    }
}
