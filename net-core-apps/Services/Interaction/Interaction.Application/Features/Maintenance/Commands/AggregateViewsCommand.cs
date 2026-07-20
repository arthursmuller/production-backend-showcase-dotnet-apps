using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Features.Maintenance.Commands;

public sealed record AggregateViewsCommand(InteractionExecutionContext Context)
    : Command<int, int>
{
    public override async Task<int> Handle(int maximum, CancellationToken cancellationToken)
    {
        if (maximum is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        var store = Context.Repository;
        await using var transaction = await store.BeginReadCommittedAsync(cancellationToken);
        var now = Context.UtcNow();
        _ = await store.DeleteExpiredViewObservationsAsync(now, maximum, cancellationToken);
        var observations = await store.LockPendingViewsAsync(now, maximum, cancellationToken);
        if (observations.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var invalidations = new List<(ResourceRef Resource, long Version)>();
        foreach (var group in observations
                     .GroupBy(item => new ResourceRef(item.ResourceType, item.ResourceId))
                     .OrderBy(item => item.Key.ToString(), StringComparer.Ordinal))
        {
            var state = await store.GetResourceStateAsync(
                group.Key, false, cancellationToken);
            if (state?.IsPubliclyInteractable == true)
            {
                var summary = await Context.GetOrCreateSummaryAsync(group.Key, cancellationToken);
                invalidations.Add((group.Key, summary.Version));
                summary.ApplyViews(group.LongCount(), Context.UtcNow());
                await Context.Publisher.PublishSummaryAsync(
                    InteractionExecutionContext.ToEvent(summary), cancellationToken);
            }
            foreach (var observation in group)
                observation.MarkProcessed(Context.UtcNow());
        }
        _ = await store.DeleteExpiredViewObservationsAsync(Context.UtcNow(), maximum, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var item in invalidations)
            await Context.TryInvalidateAsync(item.Resource, item.Version, cancellationToken);
        InteractionTelemetry.AggregatedViews.Add(observations.Count);
        return observations.Count;
    }
}
