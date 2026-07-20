using Interaction.Application.Dtos;
using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;
using Interaction.Domain.Exceptions;

namespace Interaction.Application.Features.Projections.Commands;

public sealed record ApplyContributionCommand(InteractionExecutionContext Context)
    : Command<ResourceContributionUpdate, bool>
{
    public override async Task<bool> Handle(
        ResourceContributionUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.SourceVersion <= 0)
            throw new InteractionValidationException("Contribution source version must be positive.");

        var store = Context.Repository;
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        var resourceState = await store.GetResourceStateAsync(
            update.Resource, true, cancellationToken);
        if (resourceState?.IsActive != true)
            throw new InteractionNotFoundException($"Resource '{update.Resource}' was not found.");

        var summary = await Context.GetOrCreateSummaryAsync(update.Resource, cancellationToken);
        var oldVersion = summary.Version;
        summary.ApplyContribution(
            update.CommentCount,
            update.ShareCount,
            update.SourceVersion,
            Context.UtcNow());
        if (summary.Version != oldVersion && summary.ContributionHasGap)
            InteractionTelemetry.ProjectionGaps.Add(
                1,
                new KeyValuePair<string, object?>("projection", "contribution"));
        if (summary.Version != oldVersion)
        {
            await Context.Publisher.PublishSummaryAsync(
                InteractionExecutionContext.ToEvent(summary), cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        InteractionTelemetry.RecordIngestion(
            "contribution",
            summary.Version == oldVersion
                ? "deduplicated"
                : summary.ContributionHasGap
                    ? "gap_buffered"
                    : "applied");
        if (summary.Version != oldVersion)
            await Context.TryInvalidateAsync(update.Resource, oldVersion, cancellationToken);
        return summary.Version != oldVersion;
    }
}
