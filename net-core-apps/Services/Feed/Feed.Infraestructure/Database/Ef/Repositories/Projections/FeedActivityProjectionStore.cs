using Feed.Application.Features.Feeds;
using Feed.Application.Features.Privacy;
using Feed.Domain.AggregatesModel;
using MessageBrokerContracts.Feed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class FeedActivityProjectionStore(
        FeedContext context,
        IFeedUserErasureAuthority erasureAuthority,
        IOptions<FeedOptions> options,
        FeedTelemetry telemetry,
        TimeProvider timeProvider) : IFeedActivityProjectionWriter
    {
        private readonly FeedOptions _options = options.Value;

        public async Task ApplyCanonicalAsync(
            FeedActivityChanged message,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var incoming = Validate(message);
            var observed = NormalizeUtc(observedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();
            await using var mutation = await erasureAuthority.AcquireMutationAsync(
                [incoming.ActorUserId],
                cancellationToken);
            if (!mutation.Allowed)
            {
                await mutation.CommitAsync(cancellationToken);
                telemetry.Record("activity_consume", "erased_ignored");
                return;
            }
            var activity = await context.FeedActivities.SingleOrDefaultAsync(
                item => item.SourceStreamId == incoming.StreamId,
                cancellationToken);
            var byIdentity = await context.FeedActivities.SingleOrDefaultAsync(
                item => item.TargetType == incoming.TargetType
                    && item.TargetId == incoming.TargetId
                    && item.ActivityKind == incoming.ActivityKind,
                cancellationToken);

            if (activity is not null && byIdentity is not null && activity.Id != byIdentity.Id)
            {
                await QuarantineAsync(incoming, observed, cancellationToken);
                byIdentity.Tombstone(observed);
                await EnsureActivityRevocationAsync(
                    byIdentity,
                    $"stream-conflict:{incoming.EventId:N}",
                    incoming.OccurredAtUtc,
                    observed,
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                telemetry.Record("activity_consume", "identity_conflict");
                return;
            }
            activity ??= byIdentity;
            var previousAudiencePolicy = activity?.AudiencePolicy;

            if (activity is null)
            {
                if (incoming.StreamVersion != 1)
                {
                    await QuarantineAsync(incoming, observed, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await mutation.CommitAsync(cancellationToken);
                    telemetry.Record("activity_consume", "gap");
                    return;
                }

                var deliveryMode = await ResolveDeliveryModeAsync(
                    incoming.ActorUserId,
                    observed,
                    cancellationToken);
                activity = FeedActivity.Create(
                    incoming.TargetType,
                    incoming.TargetId,
                    incoming.ActivityKind,
                    incoming.ActorUserId,
                    incoming.AudiencePolicy,
                    incoming.PublishedAtUtc,
                    observed,
                    incoming.StreamId,
                    incoming.EventId,
                    incoming.StreamVersion,
                    incoming.RankKey,
                    deliveryMode);
                if (incoming.State != FeedActivityLifecycle.Active)
                    activity.Tombstone(incoming.OccurredAtUtc);
                await context.FeedActivities.AddAsync(activity, cancellationToken);
            }
            else
            {
                if (!MatchesIdentity(activity, incoming))
                {
                    await QuarantineAsync(incoming, observed, cancellationToken);
                    activity.Tombstone(observed);
                    await EnsureActivityRevocationAsync(
                        activity,
                        $"metadata-conflict:{incoming.EventId:N}",
                        incoming.OccurredAtUtc,
                        observed,
                        cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await mutation.CommitAsync(cancellationToken);
                    telemetry.Record("activity_consume", "metadata_conflict");
                    return;
                }
                if (incoming.StreamVersion < activity.SourceVersion
                    || (incoming.StreamVersion == activity.SourceVersion
                        && incoming.EventId == activity.LastEventId))
                {
                    telemetry.Record("activity_consume", "stale_or_duplicate");
                    return;
                }
                if (incoming.StreamVersion == activity.SourceVersion)
                {
                    await QuarantineAsync(incoming, observed, cancellationToken);
                    activity.Tombstone(observed);
                    await EnsureActivityRevocationAsync(
                        activity,
                        $"version-conflict:{incoming.EventId:N}",
                        incoming.OccurredAtUtc,
                        observed,
                        cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await mutation.CommitAsync(cancellationToken);
                    telemetry.Record("activity_consume", "version_conflict");
                    return;
                }
                if (incoming.StreamVersion != activity.SourceVersion + 1)
                {
                    await QuarantineAsync(incoming, observed, cancellationToken);
                    activity.Tombstone(observed);
                    await EnsureActivityRevocationAsync(
                        activity,
                        $"gap:{incoming.EventId:N}",
                        incoming.OccurredAtUtc,
                        observed,
                        cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await mutation.CommitAsync(cancellationToken);
                    telemetry.Record("activity_consume", "gap");
                    return;
                }

                Apply(activity, incoming);
            }

            await ScheduleForStateAsync(
                activity,
                incoming,
                audienceChanged: previousAudiencePolicy is not null
                    && previousAudiencePolicy != incoming.AudiencePolicy,
                observed,
                cancellationToken);
            var replayedOccurrences = await DrainQuarantineAsync(
                activity,
                observed,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            telemetry.Record("activity_consume", "applied", activity.DeliveryMode);
            var committed = timeProvider.GetUtcNow().UtcDateTime;
            telemetry.RecordActivityConvergence(incoming.OccurredAtUtc, committed);
            foreach (var sourceOccurredAtUtc in replayedOccurrences)
                telemetry.RecordActivityConvergence(sourceOccurredAtUtc, committed);
        }

        private async Task<string> ResolveDeliveryModeAsync(
            int publisherUserId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var publisherState = await context.FeedPublisherStates.SingleOrDefaultAsync(
                state => state.PublisherUserId == publisherUserId,
                cancellationToken);
            if (publisherState is not null)
                return publisherState.DeliveryMode;

            // The first activity establishes a bounded publisher state from the local
            // projection. Subsequent relationship transitions maintain it incrementally.
            var audienceCount = await context.FeedAudienceEdges
                .Where(edge => edge.OwnerUserId == publisherUserId
                    && edge.State == "active"
                    && (edge.RelationshipKind == "follow"
                        || edge.RelationshipKind == "friend"))
                .Join(
                    context.FeedRelationshipCheckpoints.Where(checkpoint =>
                        checkpoint.Status == AudienceProjectionStates.Healthy),
                    edge => edge.SourceStreamId,
                    checkpoint => (Guid?)checkpoint.StreamId,
                    (edge, checkpoint) => new { edge, checkpoint })
                .Where(pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion)
                .Select(pair => pair.edge.ViewerUserId)
                .Distinct()
                .LongCountAsync(cancellationToken);
            publisherState = FeedPublisherState.Create(
                publisherUserId,
                audienceCount,
                _options.PullEnterAudienceCount,
                _options.PullExitAudienceCount,
                observedAtUtc);
            await context.FeedPublisherStates.AddAsync(publisherState, cancellationToken);
            return publisherState.DeliveryMode;
        }

        private async Task EnsureFanoutWorkAsync(
            FeedActivity activity,
            string sourceEventId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (activity.DeliveryMode != FeedDeliveryModes.Push
                || activity.IsTombstoned
                || activity.AudiencePolicy == FeedAudiencePolicies.Owner)
                return;
            var tracked = context.FanoutWorkItems.Local.Any(work =>
                work.SourceEventId == sourceEventId);
            var persisted = await context.FanoutWorkItems.AnyAsync(
                work => work.SourceEventId == sourceEventId,
                cancellationToken);
            if (tracked || persisted)
                return;

            await context.FanoutWorkItems.AddAsync(
                FanoutWorkItem.Create(sourceEventId, activity, observedAtUtc),
                cancellationToken);
        }

        private async Task ScheduleForStateAsync(
            FeedActivity activity,
            ValidActivityEvent incoming,
            bool audienceChanged,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (incoming.State == FeedActivityLifecycle.Active)
            {
                if (audienceChanged)
                {
                    await EnsureActivityRevocationAsync(
                        activity,
                        $"policy:{incoming.EventId:N}",
                        incoming.OccurredAtUtc,
                        observedAtUtc,
                        cancellationToken);
                    return;
                }
                await EnsureFanoutWorkAsync(
                    activity,
                    incoming.EventId.ToString("N"),
                    observedAtUtc,
                    cancellationToken);
                return;
            }

            await EnsureActivityRevocationAsync(
                activity,
                $"activity:{incoming.EventId:N}",
                incoming.OccurredAtUtc,
                observedAtUtc,
                cancellationToken);
        }

        private async Task EnsureActivityRevocationAsync(
            FeedActivity activity,
            string sourceEventId,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (context.FeedRevocationWorkItems.Local.Any(item =>
                    item.SourceEventId == sourceEventId)
                || await context.FeedRevocationWorkItems.AnyAsync(
                    item => item.SourceEventId == sourceEventId,
                    cancellationToken))
            {
                return;
            }
            await context.FeedRevocationWorkItems.AddAsync(
                FeedRevocationWorkItem.ForActivity(
                    sourceEventId,
                    activity,
                    sourceOccurredAtUtc,
                    observedAtUtc),
                cancellationToken);
        }

        private async Task QuarantineAsync(
            ValidActivityEvent incoming,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var exists = await context.FeedActivityEventQuarantines.AnyAsync(
                item => item.EventId == incoming.EventId
                    || (item.StreamId == incoming.StreamId
                        && item.StreamVersion == incoming.StreamVersion),
                cancellationToken);
            if (exists)
                return;
            await context.FeedActivityEventQuarantines.AddAsync(
                FeedActivityEventQuarantine.Create(
                    incoming.EventId,
                    incoming.StreamId,
                    incoming.StreamVersion,
                    incoming.TargetType,
                    incoming.TargetId,
                    incoming.ActivityKind,
                    incoming.ActorUserId,
                    incoming.AudiencePolicy,
                    incoming.State,
                    incoming.RankKey,
                    incoming.PublishedAtUtc,
                    incoming.OccurredAtUtc,
                    incoming.ReasonCode,
                    observedAtUtc),
                cancellationToken);
        }

        private async Task<IReadOnlyList<DateTime>> DrainQuarantineAsync(
            FeedActivity activity,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var replayedOccurrences = new List<DateTime>();
            var pending = await context.FeedActivityEventQuarantines
                .Where(item => item.StreamId == activity.SourceStreamId
                    && item.ResolvedAtUtc == null
                    && item.StreamVersion > activity.SourceVersion)
                .OrderBy(item => item.StreamVersion)
                .Take(_options.FanoutClaimBatchSize + 1)
                .ToListAsync(cancellationToken);
            foreach (var item in pending.Take(_options.FanoutClaimBatchSize))
            {
                if (item.StreamVersion != activity.SourceVersion + 1)
                    break;
                var incoming = new ValidActivityEvent(
                    item.EventId,
                    item.StreamId,
                    item.StreamVersion,
                    item.TargetType,
                    item.TargetId,
                    item.ActivityKind,
                    item.ActorUserId,
                    item.AudiencePolicy,
                    item.State,
                    item.RankKey,
                    item.PublishedAtUtc,
                    item.SourceOccurredAtUtc,
                    item.ReasonCode);
                var previousAudiencePolicy = activity.AudiencePolicy;
                Apply(activity, incoming);
                item.Resolve(observedAtUtc);
                await ScheduleForStateAsync(
                    activity,
                    incoming,
                    audienceChanged: previousAudiencePolicy != incoming.AudiencePolicy,
                    observedAtUtc,
                    cancellationToken);
                replayedOccurrences.Add(item.SourceOccurredAtUtc);
                telemetry.Record("activity_quarantine", "replayed", activity.DeliveryMode);
            }
            return replayedOccurrences;
        }

        private static void Apply(FeedActivity activity, ValidActivityEvent incoming) =>
            activity.ApplyNext(
                incoming.StreamId,
                incoming.EventId,
                incoming.StreamVersion,
                incoming.AudiencePolicy,
                incoming.State,
                incoming.OccurredAtUtc);

        private static bool MatchesIdentity(
            FeedActivity activity,
            ValidActivityEvent incoming) =>
            activity.SourceStreamId == incoming.StreamId
            && activity.TargetType == incoming.TargetType
            && activity.TargetId == incoming.TargetId
            && activity.ActivityKind == incoming.ActivityKind
            && activity.ActorUserId == incoming.ActorUserId;

        private static ValidActivityEvent Validate(FeedActivityChanged message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.EventId == Guid.Empty
                || message.StreamId == Guid.Empty
                || message.StreamVersion <= 0
                || message.ActorUserId <= 0)
            {
                throw new ArgumentException("Feed activity identity is invalid.", nameof(message));
            }
            if (string.IsNullOrWhiteSpace(message.ReasonCode)
                || message.ReasonCode.Trim().Length > FeedSchemaLimits.ErrorCodeLength)
            {
                throw new ArgumentException("A bounded reason code is required.", nameof(message));
            }
            return new ValidActivityEvent(
                message.EventId,
                message.StreamId,
                message.StreamVersion,
                NormalizeTargetType(message.TargetType),
                NormalizeTargetId(message.TargetId),
                NormalizeKind(message.ActivityKind),
                message.ActorUserId,
                NormalizeAudience(message.AudiencePolicy),
                FeedActivityLifecycle.Normalize(message.State),
                message.RankKey,
                NormalizeUtc(message.PublishedAtUtc),
                NormalizeUtc(message.OccurredAtUtc),
                message.ReasonCode.Trim().ToLowerInvariant());
        }

        private static string NormalizeAudience(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized)
                || !FeedAudiencePolicies.IsSupported(normalized))
            {
                throw new ArgumentException("Unsupported feed audience policy.", nameof(value));
            }
            return normalized;
        }

        private static string NormalizeKind(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.Length > FeedSchemaLimits.EventKindLength)
            {
                throw new ArgumentException("A bounded activity kind is required.", nameof(value));
            }
            return normalized;
        }

        private static string NormalizeTargetType(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.Length > FeedSchemaLimits.TypeLength)
            {
                throw new ArgumentException("A bounded target type is required.", nameof(value));
            }
            return normalized;
        }

        private static string NormalizeTargetId(string value)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.Length > FeedSchemaLimits.TargetIdLength)
            {
                throw new ArgumentException("A bounded target ID is required.", nameof(value));
            }
            return normalized;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default)
                throw new ArgumentException("A timestamp is required.", nameof(value));
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }

        private sealed record ValidActivityEvent(
            Guid EventId,
            Guid StreamId,
            long StreamVersion,
            string TargetType,
            string TargetId,
            string ActivityKind,
            int ActorUserId,
            string AudiencePolicy,
            string State,
            long RankKey,
            DateTime PublishedAtUtc,
            DateTime OccurredAtUtc,
            string ReasonCode);
    }
}
