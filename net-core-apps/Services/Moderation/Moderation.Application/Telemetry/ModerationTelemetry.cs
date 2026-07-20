using System.Diagnostics;
using System.Diagnostics.Metrics;
using Inspacy.Observability;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Maintenance.Commands;
using Moderation.Application.Options;

namespace Moderation.Application.Telemetry;

public static class ModerationTelemetry
{
    public const string InstrumentationName = PlatformInstrumentation.Name;
    public static readonly ActivitySource ActivitySource = PlatformInstrumentation.ActivitySource;
    private static readonly Meter Meter = PlatformInstrumentation.Meter;
    private static ModerationSlaBacklogDto _slaBacklog = new(0, 0, 0);
    private static long _slaSampleTimestampSeconds;
    private static ModerationDecisionRevisionRetentionResult _decisionRevisionRetention =
        new(0, 0, false, 0, 0, DateTimeOffset.UnixEpoch);

    internal static readonly Counter<long> ReportsReceived =
        Meter.CreateCounter<long>("moderation.reports.received");
    internal static readonly Counter<long> DuplicateReports =
        Meter.CreateCounter<long>("moderation.reports.duplicate");
    internal static readonly Counter<long> Decisions =
        Meter.CreateCounter<long>("moderation.decisions");
    internal static readonly Counter<long> WorkflowRejections =
        Meter.CreateCounter<long>(
            "moderation.workflow.rejections",
            "{rejection}",
            "Moderation state mutations rejected for a fixed workflow or version-conflict reason.");
    internal static readonly Counter<long> BoundaryCancellations =
        Meter.CreateCounter<long>(
            "moderation.operation.boundary_cancellations",
            "{cancellation}",
            "Moderation application mutations canceled at their operation boundary.");
    internal static readonly Counter<long> Escalations =
        Meter.CreateCounter<long>("moderation.cases.escalated");
    public static readonly Counter<long> RetentionRedacted =
        Meter.CreateCounter<long>("moderation.retention.cases.redacted");
    public static readonly Counter<long> RetentionPurged =
        Meter.CreateCounter<long>("moderation.retention.cases.purged");
    public static readonly Counter<long> PrunedDecisionRevisions =
        Meter.CreateCounter<long>("moderation.decision_revision.pruned", "{revision}");
    public static readonly Counter<long> ErasureRequests =
        Meter.CreateCounter<long>("moderation.erasure.requests");
    public static readonly Histogram<int> ErasureBatchWork =
        Meter.CreateHistogram<int>("moderation.erasure.batch.work", "{work_item}");
    public static readonly Counter<long> ErasureCompletions =
        Meter.CreateCounter<long>("moderation.erasure.completions");
    public static readonly Histogram<int> ErasureHeldCases =
        Meter.CreateHistogram<int>("moderation.erasure.held_cases", "{case}");
    internal static readonly Histogram<double> OperationDuration =
        Meter.CreateHistogram<double>("moderation.operation.duration", "ms");
    private static readonly Histogram<double> FirstResponseDuration =
        Meter.CreateHistogram<double>(
            "moderation.first_response.duration",
            "s",
            "Elapsed time from report receipt to the first successful triage response.");
    private static readonly Counter<long> FirstResponseBreaches =
        Meter.CreateCounter<long>(
            "moderation.first_response.breaches",
            "{breach}",
            "First responses exceeding the committed four-hour objective.");
    private static readonly ObservableGauge<long> ActiveSlaCount =
        Meter.CreateObservableGauge(
            "moderation.sla.active",
            () => Volatile.Read(ref _slaBacklog).ActiveCount,
            "{case}",
            "Aggregate count of non-terminal cases with an active SLA.");
    private static readonly ObservableGauge<long> OverdueSlaCount =
        Meter.CreateObservableGauge(
            "moderation.sla.overdue",
            () => Volatile.Read(ref _slaBacklog).OverdueCount,
            "{case}",
            "Aggregate count of active cases beyond their current SLA due time.");
    private static readonly ObservableGauge<double> OldestOverdueSlaAge =
        Meter.CreateObservableGauge(
            "moderation.sla.oldest_overdue_age",
            () => Volatile.Read(ref _slaBacklog).OldestOverdueAgeSeconds,
            "s",
            "Age beyond the due time of the oldest active overdue case.");
    private static readonly ObservableGauge<long> SlaSampleTimestamp =
        Meter.CreateObservableGauge(
            "moderation.sla.sample_timestamp",
            () => Volatile.Read(ref _slaSampleTimestampSeconds),
            "s",
            "Unix timestamp of the latest successful aggregate SLA sample.");
    private static readonly ObservableGauge<long> DecisionRevisionTotal =
        Meter.CreateObservableGauge(
            "moderation.decision_revision.total_estimate",
            () => Volatile.Read(ref _decisionRevisionRetention).EstimatedTotalRevisionCount,
            "{revision}");
    private static readonly ObservableGauge<long> DecisionRevisionEligible =
        Meter.CreateObservableGauge(
            "moderation.decision_revision.eligible_lower_bound",
            () => (long)Volatile.Read(ref _decisionRevisionRetention).EligibleRevisionCountLowerBound,
            "{revision}",
            "Bounded sample count; equals the batch cap when more eligible revisions may remain.");
    private static readonly ObservableGauge<double> DecisionRevisionOldestEligibleAge =
        Meter.CreateObservableGauge(
            "moderation.decision_revision.oldest_eligible_age",
            () => Volatile.Read(ref _decisionRevisionRetention).OldestEligibleAgeSeconds,
            "s");
    private static readonly ObservableGauge<long> DecisionRevisionSampleTimestamp =
        Meter.CreateObservableGauge(
            "moderation.decision_revision.sample_timestamp",
            () => Volatile.Read(ref _decisionRevisionRetention).SampledAtUtc.ToUnixTimeSeconds(),
            "s");

    public static void RecordFirstResponse(TimeSpan duration)
    {
        var seconds = Math.Max(0, duration.TotalSeconds);
        FirstResponseDuration.Record(seconds);
        if (seconds > TimeSpan.FromMinutes(
                ModerationSlaOptions.FirstResponseMaximumMinutes).TotalSeconds)
        {
            FirstResponseBreaches.Add(1);
        }
    }

    public static void SetSlaBacklog(ModerationSlaBacklogDto snapshot)
    {
        Interlocked.Exchange(ref _slaBacklog, snapshot);
        Interlocked.Exchange(
            ref _slaSampleTimestampSeconds,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public static void SetDecisionRevisionRetention(
        ModerationDecisionRevisionRetentionResult snapshot) =>
        Interlocked.Exchange(ref _decisionRevisionRetention, snapshot);

    internal static void RecordWorkflowRejection(string reason) =>
        WorkflowRejections.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason));

    internal static void RecordBoundaryCancellation() =>
        BoundaryCancellations.Add(
            1,
            new KeyValuePair<string, object?>("boundary", "application_mutation"));
}
