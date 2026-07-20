using Interaction.Application.Dtos;
using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;

namespace Interaction.Application.Features.Projections.Commands;

public sealed record ApplyResourceStateCommand(InteractionExecutionContext Context)
    : Command<ResourceStateUpdate, bool>
{
    public override async Task<bool> Handle(
        ResourceStateUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.SourceVersion <= 0)
            throw new InteractionValidationException("Source version must be positive.");

        var store = Context.Repository;
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        var state = await store.GetResourceStateAsync(update.Resource, true, cancellationToken);
        if (state is null)
        {
            state = new ResourceState(update.Resource, Context.UtcNow());
            store.AddResourceState(state);
        }
        if (update.SourceVersion <= state.AppliedVersion)
        {
            var cleanup = await EraseDeletedResourceAsync(state, cancellationToken);
            if (cleanup.Changed)
                await store.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            InteractionTelemetry.RecordIngestion("resource_state", "deduplicated");
            if (cleanup.Changed)
                await Context.TryInvalidateAsync(update.Resource, cleanup.OldSummaryVersion, cancellationToken);
            return cleanup.Changed;
        }

        var ingestionOutcome = "applied";
        if (update.SourceVersion > state.AppliedVersion + 1)
        {
            var gapAdded = false;
            if (await store.GetResourceGapAsync(
                    update.Resource, update.SourceVersion, cancellationToken) is null
                && state.ObserveGap(update.SourceVersion, Context.UtcNow()))
            {
                gapAdded = true;
                InteractionTelemetry.ProjectionGaps.Add(
                    1,
                    new KeyValuePair<string, object?>("projection", "resource_state"));
                store.AddResourceGap(new ResourceStateGap(
                    update.Resource,
                    update.SourceVersion,
                    update.Lifecycle,
                    update.Visibility,
                    update.OccurredAtUtc,
                    Context.UtcNow()));
            }
            ingestionOutcome = gapAdded ? "gap_buffered" : "deduplicated";
        }
        else
        {
            state.ApplyNext(
                update.Lifecycle,
                update.Visibility,
                update.SourceVersion,
                Context.UtcNow());
            for (var drained = 0; drained < Context.Options.ProjectionGapDrainMaximum; drained++)
            {
                var gap = await store.GetResourceGapAsync(
                    update.Resource, state.AppliedVersion + 1, cancellationToken);
                if (gap is null)
                    break;
                state.ApplyNext(
                    gap.Lifecycle, gap.Visibility, gap.SourceVersion, Context.UtcNow());
                store.RemoveResourceGap(gap);
            }
            state.RefreshGapState();
        }

        var deletedCleanup = await EraseDeletedResourceAsync(state, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        InteractionTelemetry.RecordIngestion("resource_state", ingestionOutcome);
        if (deletedCleanup.Changed)
            await Context.TryInvalidateAsync(update.Resource, deletedCleanup.OldSummaryVersion, cancellationToken);
        return true;
    }

    private async Task<(bool Changed, long OldSummaryVersion)> EraseDeletedResourceAsync(
        ResourceState state,
        CancellationToken cancellationToken)
    {
        if (!state.IsDeleted)
            return (false, 0);

        var store = Context.Repository;
        var summary = await Context.GetOrCreateSummaryAsync(state.Resource, cancellationToken);
        var oldVersion = summary.Version;
        var removed = await store.PurgeResourcePersonalDataAsync(state.Resource, cancellationToken);
        var summaryChanged = summary.EraseForDeletedResource(Context.UtcNow());
        if (summaryChanged)
        {
            await Context.Publisher.PublishSummaryAsync(
                InteractionExecutionContext.ToEvent(summary),
                cancellationToken);
        }
        return (removed > 0 || summaryChanged, oldVersion);
    }
}
