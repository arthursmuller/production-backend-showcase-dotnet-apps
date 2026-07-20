using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Inspacy.Observability;

namespace Comment.Infra.Persistence.Visibility
{
    public sealed class CommentAudienceProjectionTelemetry
    {
        private static readonly Meter Meter = PlatformInstrumentation.Meter;
        private static readonly Counter<long> Events = Meter.CreateCounter<long>(
            "inspacy.comment.audience_projection.outcomes", unit: "{event}");
        private static readonly Counter<long> AuthorizationOutcomes = Meter.CreateCounter<long>(
            "inspacy.comment.audience_authorization.outcomes", unit: "{decision}");
        private static readonly Counter<long> BootstrapOutcomes = Meter.CreateCounter<long>(
            "inspacy.comment.audience_bootstrap.outcomes", unit: "{event}");

        public void Event(string kind, string outcome) => Events.Add(1,
            new KeyValuePair<string, object?>("relationship.kind", LowCardinality(kind)),
            new KeyValuePair<string, object?>("projection.outcome", LowCardinality(outcome)));

        public void Authorization(string audience, string outcome) => AuthorizationOutcomes.Add(1,
            new KeyValuePair<string, object?>("audience.policy", LowCardinality(audience)),
            new KeyValuePair<string, object?>("authorization.outcome", LowCardinality(outcome)));

        public void Bootstrap(string outcome) => BootstrapOutcomes.Add(1,
            new KeyValuePair<string, object?>("bootstrap.outcome", LowCardinality(outcome)));

        private static string LowCardinality(string value) => value switch
        {
            "follow" or "friend" or "block" or "public" or "friends" or "friends_of_friends"
                or "owner" or "applied" or "duplicate" or "gap" or "invalid" or "allowed"
                or "denied" or "unavailable" or "page" or "complete" or "failed" or "lease_busy" => value,
            _ => "other",
        };
    }
}
