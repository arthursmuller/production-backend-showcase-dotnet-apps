using System;
using System.Diagnostics.Metrics;
using System.Collections.Generic;
using Inspacy.Observability;

namespace Feed.Infra.Persistence
{
    public sealed class AudienceProjectionTelemetry
    {
        public const string ConvergenceDurationName =
            "inspacy.feed.audience_projection.convergence.duration";
        public const string ConvergenceBreachName =
            "inspacy.feed.audience_projection.convergence.breaches";

        private static readonly Meter Meter = PlatformInstrumentation.Meter;
        private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
            "inspacy.feed.audience_projection.outcomes",
            unit: "{event}",
            description: "Following relationship projection outcomes in Feed.");
        private static readonly Counter<long> BootstrapOutcomes = Meter.CreateCounter<long>(
            "inspacy.feed.audience_bootstrap.outcomes",
            unit: "{event}",
            description: "Durable Feed audience bootstrap outcomes.");
        private static readonly Counter<long> AuthorizationOutcomes = Meter.CreateCounter<long>(
            "inspacy.feed.audience_authorization.outcomes",
            unit: "{decision}",
            description: "Low-cardinality Feed audience authorization decisions.");
        private static readonly Histogram<double> ConvergenceDuration =
            Meter.CreateHistogram<double>(
                ConvergenceDurationName,
                unit: "s",
                description: "Following event or snapshot to committed Feed audience projection latency.");
        private static readonly Counter<long> ConvergenceBreaches =
            Meter.CreateCounter<long>(
                ConvergenceBreachName,
                unit: "{breach}",
                description: "Feed audience projections exceeding the exact 60-second convergence budget.");

        public void Record(string relationshipKind, string outcome) =>
            Outcomes.Add(
                1,
                new KeyValuePair<string, object?>("relationship.kind", relationshipKind),
                new KeyValuePair<string, object?>("projection.outcome", outcome));

        public void RecordBootstrap(string outcome) =>
            BootstrapOutcomes.Add(
                1,
                new KeyValuePair<string, object?>("bootstrap.outcome", outcome));

        public void RecordAuthorization(string policy, string outcome) =>
            AuthorizationOutcomes.Add(
                1,
                new KeyValuePair<string, object?>("audience.policy", policy),
                new KeyValuePair<string, object?>("authorization.outcome", outcome));

        public void RecordConvergence(
            string projectionSource,
            DateTime sourceOccurredAtUtc,
            DateTime committedAtUtc)
        {
            var source = projectionSource switch
            {
                "event" => "event",
                "snapshot" => "snapshot",
                _ => "unknown",
            };
            var tags = new KeyValuePair<string, object?>("projection.source", source);
            var measurement = FeedConvergenceMeasurement.Create(
                sourceOccurredAtUtc,
                committedAtUtc,
                TimeSpan.FromSeconds(60));
            ConvergenceDuration.Record(measurement.DurationSeconds, tags);
            if (measurement.Breached)
                ConvergenceBreaches.Add(1, tags);
        }
    }
}
