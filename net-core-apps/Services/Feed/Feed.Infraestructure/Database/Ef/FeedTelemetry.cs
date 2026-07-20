using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Inspacy.Observability;

namespace Feed.Infra.Persistence
{
    public sealed class FeedTelemetry
    {
        public const string ActivityConvergenceDurationName =
            "inspacy.feed.activity_projection.convergence.duration";
        public const string ActivityConvergenceBreachName =
            "inspacy.feed.activity_projection.convergence.breaches";
        public const string RevocationCompletionDurationName =
            "inspacy.feed.revocation.completion.duration";
        public const string RevocationCompletionBreachName =
            "inspacy.feed.revocation.completion.breaches";

        private static readonly Meter Meter = PlatformInstrumentation.Meter;
        private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
            "inspacy.feed.outcomes",
            unit: "{operation}",
            description: "Bounded Feed operation outcomes.");
        private static readonly Histogram<long> BatchSizes = Meter.CreateHistogram<long>(
            "inspacy.feed.batch.size",
            unit: "{item}",
            description: "Feed bounded batch sizes.");
        private static readonly Histogram<double> ActivityConvergenceDuration =
            Meter.CreateHistogram<double>(
                ActivityConvergenceDurationName,
                unit: "s",
                description: "Canonical Feed activity source-event to committed queryable projection latency.");
        private static readonly Counter<long> ActivityConvergenceBreaches =
            Meter.CreateCounter<long>(
                ActivityConvergenceBreachName,
                unit: "{breach}",
                description: "Canonical Feed activity projections exceeding the exact 60-second convergence budget.");
        private static readonly Histogram<double> RevocationCompletionDuration =
            Meter.CreateHistogram<double>(
                RevocationCompletionDurationName,
                unit: "s",
                description: "Authoritative source-event to committed final Feed revocation sweep latency.");
        private static readonly Counter<long> RevocationCompletionBreaches =
            Meter.CreateCounter<long>(
                RevocationCompletionBreachName,
                unit: "{breach}",
                description: "Final Feed revocation sweeps exceeding the exact five-second privacy budget.");

        public void Record(string operation, string outcome, string mode = "none") =>
            Outcomes.Add(
                1,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("outcome", outcome),
                new KeyValuePair<string, object?>("mode", mode));

        public void RecordBatch(string operation, string mode, int size) =>
            BatchSizes.Record(
                size,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("mode", mode));

        public void RecordBudgetRejected(string reason) =>
            Record("read_budget", "rejected", reason);

        public void RecordActivityConvergence(
            DateTime sourceOccurredAtUtc,
            DateTime committedAtUtc)
        {
            var measurement = FeedConvergenceMeasurement.Create(
                sourceOccurredAtUtc,
                committedAtUtc,
                TimeSpan.FromSeconds(60));
            ActivityConvergenceDuration.Record(measurement.DurationSeconds);
            if (measurement.Breached)
                ActivityConvergenceBreaches.Add(1);
        }

        public void RecordRevocationCompletion(
            string revocationKind,
            DateTime sourceOccurredAtUtc,
            DateTime committedAtUtc)
        {
            var kind = revocationKind switch
            {
                Feed.Domain.AggregatesModel.FeedRevocationKinds.Activity =>
                    Feed.Domain.AggregatesModel.FeedRevocationKinds.Activity,
                Feed.Domain.AggregatesModel.FeedRevocationKinds.Relationship =>
                    Feed.Domain.AggregatesModel.FeedRevocationKinds.Relationship,
                Feed.Domain.AggregatesModel.FeedRevocationKinds.Block =>
                    Feed.Domain.AggregatesModel.FeedRevocationKinds.Block,
                Feed.Domain.AggregatesModel.FeedRevocationKinds.FriendsOfFriends =>
                    Feed.Domain.AggregatesModel.FeedRevocationKinds.FriendsOfFriends,
                _ => "unknown",
            };
            var tags = new KeyValuePair<string, object?>("revocation.kind", kind);
            var measurement = FeedConvergenceMeasurement.Create(
                sourceOccurredAtUtc,
                committedAtUtc,
                TimeSpan.FromSeconds(5));
            RevocationCompletionDuration.Record(measurement.DurationSeconds, tags);
            if (measurement.Breached)
                RevocationCompletionBreaches.Add(1, tags);
        }
    }

    internal readonly record struct FeedConvergenceMeasurement(
        double DurationSeconds,
        bool Breached)
    {
        private const double MaximumObservableSeconds = 30 * 24 * 60 * 60;

        public static FeedConvergenceMeasurement Create(
            DateTime sourceOccurredAtUtc,
            DateTime committedAtUtc,
            TimeSpan budget)
        {
            if (sourceOccurredAtUtc == default || committedAtUtc == default)
                return new(0, false);

            var source = NormalizeUtc(sourceOccurredAtUtc);
            var committed = NormalizeUtc(committedAtUtc);
            var actualSeconds = Math.Max(0, (committed - source).TotalSeconds);
            return new(
                Math.Min(actualSeconds, MaximumObservableSeconds),
                actualSeconds > budget.TotalSeconds);
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
