using System.Diagnostics.Metrics;
using Inspacy.Observability;
using Interaction.Application.Dtos;
using Interaction.Application.Features.Maintenance.Commands;

namespace Interaction.Application.Telemetry;

internal static class InteractionTelemetry
{
    private static readonly Meter Meter = PlatformInstrumentation.Meter;
    private static ProjectionGapBacklogDto _projectionGapBacklog = new(0, 0);
    private static long _projectionGapSampleTimestampSeconds;
    private static InteractionAuthorityRevisionRetentionResult _authorityRevisionRetention =
        new(0, 0, false, 0, 0, DateTimeOffset.UnixEpoch);
    internal static readonly Counter<long> ViewObservations = Meter.CreateCounter<long>(
        "inspacy.interaction.view.observations", "{observation}");
    internal static readonly Counter<long> AggregatedViews = Meter.CreateCounter<long>(
        "inspacy.interaction.view.aggregated", "{view}");
    internal static readonly Counter<long> ProjectionGaps = Meter.CreateCounter<long>(
        "inspacy.interaction.projection.gaps", "{gap}");
    internal static readonly Counter<long> IngestionOutcomes = Meter.CreateCounter<long>(
        "inspacy.interaction.ingestion.outcomes",
        "{event}",
        "Interaction ingestion outcomes grouped by fixed event kind and outcome values.");
    internal static readonly Counter<long> ReconciledSummaries = Meter.CreateCounter<long>(
        "inspacy.interaction.summary.reconciled", "{summary}");
    internal static readonly Histogram<long> ReconciliationDrift = Meter.CreateHistogram<long>(
        "inspacy.interaction.reconciliation.drift",
        description: "Absolute counter drift found during authoritative reconciliation.");
    internal static readonly Histogram<int> StatusBatchSize = Meter.CreateHistogram<int>(
        "inspacy.interaction.status.batch.size", "{resource}");
    internal static readonly Counter<long> ErasureRequests = Meter.CreateCounter<long>(
        "inspacy.interaction.erasure.requests", "{request}");
    internal static readonly Histogram<int> ErasureBatchWork = Meter.CreateHistogram<int>(
        "inspacy.interaction.erasure.batch.work", "{work_item}");
    internal static readonly Counter<long> ErasureCompletions = Meter.CreateCounter<long>(
        "inspacy.interaction.erasure.completions", "{completion}");
    internal static readonly Counter<long> PrunedAuthorityRevisions = Meter.CreateCounter<long>(
        "inspacy.interaction.authority_revision.pruned", "{revision}");

    private static readonly ObservableGauge<long> UnresolvedProjectionGaps =
        Meter.CreateObservableGauge(
            "inspacy.interaction.projection.gap.unresolved",
            () => Volatile.Read(ref _projectionGapBacklog).PendingCount,
            "{gap}",
            "Number of unresolved resource projection gaps.");
    private static readonly ObservableGauge<double> OldestProjectionGapAge =
        Meter.CreateObservableGauge(
            "inspacy.interaction.projection.gap.oldest_age",
            () => Volatile.Read(ref _projectionGapBacklog).OldestAgeSeconds,
            "s",
            "Age of the oldest unresolved resource projection gap.");
    private static readonly ObservableGauge<long> ProjectionGapSampleTimestamp =
        Meter.CreateObservableGauge(
            "inspacy.interaction.projection.gap.sample_timestamp",
            () => Volatile.Read(ref _projectionGapSampleTimestampSeconds),
            "s",
            "Unix timestamp of the latest successful unresolved-gap sample.");
    private static readonly ObservableGauge<long> AuthorityRevisionTotal =
        Meter.CreateObservableGauge(
            "inspacy.interaction.authority_revision.total_estimate",
            () => Volatile.Read(ref _authorityRevisionRetention).EstimatedTotalRevisionCount,
            "{revision}");
    private static readonly ObservableGauge<long> AuthorityRevisionEligible =
        Meter.CreateObservableGauge(
            "inspacy.interaction.authority_revision.eligible_lower_bound",
            () => (long)Volatile.Read(ref _authorityRevisionRetention).EligibleRevisionCountLowerBound,
            "{revision}",
            "Bounded sample count; equals the batch cap when more eligible revisions may remain.");
    private static readonly ObservableGauge<double> AuthorityRevisionOldestEligibleAge =
        Meter.CreateObservableGauge(
            "inspacy.interaction.authority_revision.oldest_eligible_age",
            () => Volatile.Read(ref _authorityRevisionRetention).OldestEligibleAgeSeconds,
            "s");
    private static readonly ObservableGauge<long> AuthorityRevisionSampleTimestamp =
        Meter.CreateObservableGauge(
            "inspacy.interaction.authority_revision.sample_timestamp",
            () => Volatile.Read(ref _authorityRevisionRetention).SampledAtUtc.ToUnixTimeSeconds(),
            "s");

    internal static void SetProjectionGapBacklog(ProjectionGapBacklogDto snapshot)
    {
        Interlocked.Exchange(ref _projectionGapBacklog, snapshot);
        Interlocked.Exchange(
            ref _projectionGapSampleTimestampSeconds,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    internal static void SetAuthorityRevisionRetention(
        InteractionAuthorityRevisionRetentionResult snapshot) =>
        Interlocked.Exchange(ref _authorityRevisionRetention, snapshot);

    internal static void RecordIngestion(string kind, string outcome) =>
        IngestionOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("ingestion_kind", kind),
            new KeyValuePair<string, object?>("ingestion_outcome", outcome));

    internal static void RecordReconciliationDrift(
        string counter,
        long projected,
        long authoritative)
    {
        var difference = projected >= authoritative
            ? projected - authoritative
            : authoritative - projected;
        if (difference > 0)
        {
            ReconciliationDrift.Record(
                difference,
                new KeyValuePair<string, object?>("counter", counter));
        }
    }
}
