using Feed.Application.Dtos;
using Feed.Application.Features.Engagement;
using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class EngagementSummaryProjectionStore(
        FeedContext context,
        FeedTelemetry telemetry) : IEngagementSummaryProjectionWriter
    {
        private const int MaximumRetainedGapEventsPerResource = 100;

        public async Task<EngagementProjectionResult> ApplyAsync(
            EngagementSummaryProjectionDto summary,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var incoming = Validate(summary);
            var observed = NormalizeUtc(observedAtUtc, nameof(observedAtUtc));
            cancellationToken.ThrowIfCancellationRequested();

            IDbContextTransaction? ownedTransaction = null;
            if (context.Database.CurrentTransaction is null)
            {
                ownedTransaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            }

            await using (ownedTransaction)
            {
                await EnsureCheckpointAsync(incoming, observed, cancellationToken);
                var checkpoint = await LockCheckpointAsync(incoming, cancellationToken);
                var duplicateEvent = await context.FeedEngagementSummaryEventQuarantines
                    .SingleOrDefaultAsync(
                        item => item.EventId == incoming.EventId,
                        cancellationToken);
                if (duplicateEvent is not null)
                {
                    if (SameResource(duplicateEvent, incoming)
                        && duplicateEvent.Matches(
                            incoming.EventId,
                            incoming.LikeCount,
                            incoming.DislikeCount,
                            incoming.RatingCount,
                            incoming.RatingTotal,
                            incoming.ViewCount,
                            incoming.CommentCount,
                            incoming.ShareCount,
                            incoming.SummaryVersion,
                            incoming.SourceOccurredAtUtc))
                    {
                        return await CompleteAsync(
                            checkpoint,
                            EngagementProjectionOutcome.StaleOrDuplicate,
                            "stale_or_duplicate",
                            ownedTransaction,
                            cancellationToken);
                    }

                    checkpoint.MarkQuarantined(incoming.SummaryVersion, observed);
                    return await CompleteAsync(
                        checkpoint,
                        EngagementProjectionOutcome.InvalidQuarantined,
                        "event_identity_conflict",
                        ownedTransaction,
                        cancellationToken);
                }

                var disposition = checkpoint.Classify(incoming.SummaryVersion);
                if (disposition == FeedEngagementVersionDisposition.Quarantined)
                {
                    return await CompleteAsync(
                        checkpoint,
                        EngagementProjectionOutcome.InvalidQuarantined,
                        "projection_quarantined",
                        ownedTransaction,
                        cancellationToken);
                }

                if (disposition == FeedEngagementVersionDisposition.StaleOrDuplicate)
                {
                    if (incoming.SummaryVersion == checkpoint.LastAppliedVersion
                        && !checkpoint.Matches(
                            incoming.EventId,
                            incoming.LikeCount,
                            incoming.DislikeCount,
                            incoming.RatingCount,
                            incoming.RatingTotal,
                            incoming.ViewCount,
                            incoming.CommentCount,
                            incoming.ShareCount,
                            incoming.SummaryVersion,
                            incoming.SourceOccurredAtUtc))
                    {
                        await AddQuarantineAsync(
                            incoming,
                            checkpoint.LastAppliedVersion + 1,
                            "version_replay_conflict",
                            observed,
                            cancellationToken);
                        checkpoint.MarkQuarantined(incoming.SummaryVersion, observed);
                        return await CompleteAsync(
                            checkpoint,
                            EngagementProjectionOutcome.InvalidQuarantined,
                            "version_replay_conflict",
                            ownedTransaction,
                            cancellationToken);
                    }

                    return await CompleteAsync(
                        checkpoint,
                        EngagementProjectionOutcome.StaleOrDuplicate,
                        "stale_or_duplicate",
                        ownedTransaction,
                        cancellationToken);
                }

                if (disposition == FeedEngagementVersionDisposition.Gap)
                {
                    var sameVersion = await context.FeedEngagementSummaryEventQuarantines
                        .Where(item => item.ResourceType == incoming.ResourceType
                            && item.ResourceId == incoming.ResourceId
                            && item.SummaryVersion == incoming.SummaryVersion
                            && item.ResolvedAtUtc == null)
                        .OrderBy(item => item.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (sameVersion is not null)
                    {
                        if (sameVersion.Matches(
                            incoming.EventId,
                            incoming.LikeCount,
                            incoming.DislikeCount,
                            incoming.RatingCount,
                            incoming.RatingTotal,
                            incoming.ViewCount,
                            incoming.CommentCount,
                            incoming.ShareCount,
                            incoming.SummaryVersion,
                            incoming.SourceOccurredAtUtc))
                        {
                            return await CompleteAsync(
                                checkpoint,
                                EngagementProjectionOutcome.StaleOrDuplicate,
                                "stale_or_duplicate",
                                ownedTransaction,
                                cancellationToken);
                        }

                        await AddQuarantineAsync(
                            incoming,
                            checkpoint.LastAppliedVersion + 1,
                            "version_replay_conflict",
                            observed,
                            cancellationToken);
                        checkpoint.MarkQuarantined(incoming.SummaryVersion, observed);
                        return await CompleteAsync(
                            checkpoint,
                            EngagementProjectionOutcome.InvalidQuarantined,
                            "version_replay_conflict",
                            ownedTransaction,
                            cancellationToken);
                    }

                    var retainedCount = await context.FeedEngagementSummaryEventQuarantines
                        .CountAsync(item => item.ResourceType == incoming.ResourceType
                            && item.ResourceId == incoming.ResourceId
                            && item.ResolvedAtUtc == null,
                            cancellationToken);
                    if (retainedCount >= MaximumRetainedGapEventsPerResource)
                    {
                        await AddQuarantineAsync(
                            incoming,
                            checkpoint.LastAppliedVersion + 1,
                            "gap_retention_limit",
                            observed,
                            cancellationToken);
                        checkpoint.MarkQuarantined(incoming.SummaryVersion, observed);
                        return await CompleteAsync(
                            checkpoint,
                            EngagementProjectionOutcome.InvalidQuarantined,
                            "gap_retention_limit",
                            ownedTransaction,
                            cancellationToken);
                    }

                    await AddQuarantineAsync(
                        incoming,
                        checkpoint.LastAppliedVersion + 1,
                        "version_gap",
                        observed,
                        cancellationToken);
                    checkpoint.MarkGap(incoming.SummaryVersion, observed);
                    return await CompleteAsync(
                        checkpoint,
                        EngagementProjectionOutcome.GapRetained,
                        "gap_retained",
                        ownedTransaction,
                        cancellationToken);
                }

                var wasGapped = checkpoint.Status == FeedEngagementProjectionStates.Gap;
                var retainedCurrent = await context.FeedEngagementSummaryEventQuarantines
                    .Where(item => item.ResourceType == incoming.ResourceType
                        && item.ResourceId == incoming.ResourceId
                        && item.SummaryVersion == incoming.SummaryVersion
                        && item.ResolvedAtUtc == null)
                    .OrderBy(item => item.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (retainedCurrent is not null
                    && !retainedCurrent.Matches(
                        incoming.EventId,
                        incoming.LikeCount,
                        incoming.DislikeCount,
                        incoming.RatingCount,
                        incoming.RatingTotal,
                        incoming.ViewCount,
                        incoming.CommentCount,
                        incoming.ShareCount,
                        incoming.SummaryVersion,
                        incoming.SourceOccurredAtUtc))
                {
                    await AddQuarantineAsync(
                        incoming,
                        checkpoint.LastAppliedVersion + 1,
                        "version_replay_conflict",
                        observed,
                        cancellationToken);
                    checkpoint.MarkQuarantined(incoming.SummaryVersion, observed);
                    return await CompleteAsync(
                        checkpoint,
                        EngagementProjectionOutcome.InvalidQuarantined,
                        "version_replay_conflict",
                        ownedTransaction,
                        cancellationToken);
                }

                Apply(checkpoint, incoming, observed);
                retainedCurrent?.Resolve("applied_by_redelivery", observed);

                var retained = await context.FeedEngagementSummaryEventQuarantines
                    .Where(item => item.ResourceType == incoming.ResourceType
                        && item.ResourceId == incoming.ResourceId
                        && item.ResolvedAtUtc == null
                        && item.ReasonCode == "version_gap")
                    .OrderBy(item => item.SummaryVersion)
                    .ThenBy(item => item.Id)
                    .Take(MaximumRetainedGapEventsPerResource + 1)
                    .ToListAsync(cancellationToken);
                var drained = 0;
                foreach (var pending in retained)
                {
                    if (pending.SummaryVersion <= checkpoint.LastAppliedVersion)
                    {
                        pending.Resolve("stale_after_recovery", observed);
                        continue;
                    }
                    if (pending.SummaryVersion != checkpoint.LastAppliedVersion + 1)
                        break;

                    Apply(checkpoint, pending, observed);
                    pending.Resolve("gap_recovered", observed);
                    drained++;
                }

                // Use the tracked, bounded drain set: resolved rows have not been flushed yet,
                // so another database query would incorrectly rediscover them as unresolved.
                var nextRetainedVersion = retained
                    .Where(item => item.ResolvedAtUtc == null)
                    .Select(item => (long?)item.SummaryVersion)
                    .Min();
                if (nextRetainedVersion.HasValue)
                {
                    if (nextRetainedVersion.Value <= checkpoint.LastAppliedVersion + 1)
                    {
                        checkpoint.MarkQuarantined(nextRetainedVersion.Value, observed);
                    }
                    else
                    {
                        checkpoint.MarkGap(nextRetainedVersion.Value, observed);
                    }
                }

                var recovered = (wasGapped || drained > 0)
                    && checkpoint.Status == FeedEngagementProjectionStates.Healthy;
                return await CompleteAsync(
                    checkpoint,
                    recovered
                        ? EngagementProjectionOutcome.GapRecovered
                        : EngagementProjectionOutcome.Applied,
                    recovered ? "gap_recovered" : "applied",
                    ownedTransaction,
                    cancellationToken);
            }
        }

        private async Task EnsureCheckpointAsync(
            ValidSummary incoming,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT IGNORE INTO `FeedEngagementSummary`
                    (`ResourceType`, `ResourceId`, `LikeCount`, `DislikeCount`,
                     `RatingCount`, `RatingTotal`, `ViewCount`, `CommentCount`,
                     `ShareCount`, `LastAppliedVersion`, `HighestObservedVersion`,
                     `Status`, `GapFromVersion`, `GapToVersion`, `LastEventId`,
                     `LastSourceOccurredAtUtc`, `CreatedAtUtc`, `UpdatedAtUtc`, `Version`)
                VALUES
                    ({incoming.ResourceType}, {incoming.ResourceId}, 0, 0, 0, 0, 0, 0, 0,
                     0, 0, {FeedEngagementProjectionStates.Uninitialized}, NULL, NULL, NULL,
                     NULL, {observedAtUtc}, {observedAtUtc}, 1)
                """, cancellationToken);
        }

        private Task<FeedEngagementSummary> LockCheckpointAsync(
            ValidSummary incoming,
            CancellationToken cancellationToken) =>
            context.FeedEngagementSummaries
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM `FeedEngagementSummary`
                    WHERE `ResourceType` = {incoming.ResourceType}
                      AND `ResourceId` = {incoming.ResourceId}
                    FOR UPDATE
                    """)
                .SingleAsync(cancellationToken);

        private async Task AddQuarantineAsync(
            ValidSummary incoming,
            long expectedVersion,
            string reasonCode,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var item = FeedEngagementSummaryEventQuarantine.Create(
                incoming.EventId,
                incoming.ResourceType,
                incoming.ResourceId,
                incoming.LikeCount,
                incoming.DislikeCount,
                incoming.RatingCount,
                incoming.RatingTotal,
                incoming.ViewCount,
                incoming.CommentCount,
                incoming.ShareCount,
                incoming.SummaryVersion,
                expectedVersion,
                reasonCode,
                incoming.SourceOccurredAtUtc,
                observedAtUtc);
            await context.FeedEngagementSummaryEventQuarantines.AddAsync(
                item,
                cancellationToken);
        }

        private async Task<EngagementProjectionResult> CompleteAsync(
            FeedEngagementSummary checkpoint,
            EngagementProjectionOutcome outcome,
            string telemetryOutcome,
            IDbContextTransaction? ownedTransaction,
            CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            telemetry.Record("engagement_projection", telemetryOutcome, checkpoint.ResourceType);
            return new EngagementProjectionResult(
                outcome,
                checkpoint.ResourceType,
                checkpoint.ResourceId,
                checkpoint.LastAppliedVersion,
                checkpoint.HighestObservedVersion,
                checkpoint.Status);
        }

        private static void Apply(
            FeedEngagementSummary checkpoint,
            ValidSummary incoming,
            DateTime observedAtUtc) =>
            checkpoint.ApplyNext(
                incoming.EventId,
                incoming.LikeCount,
                incoming.DislikeCount,
                incoming.RatingCount,
                incoming.RatingTotal,
                incoming.ViewCount,
                incoming.CommentCount,
                incoming.ShareCount,
                incoming.SummaryVersion,
                incoming.SourceOccurredAtUtc,
                observedAtUtc);

        private static void Apply(
            FeedEngagementSummary checkpoint,
            FeedEngagementSummaryEventQuarantine incoming,
            DateTime observedAtUtc) =>
            checkpoint.ApplyNext(
                incoming.EventId,
                incoming.LikeCount,
                incoming.DislikeCount,
                incoming.RatingCount,
                incoming.RatingTotal,
                incoming.ViewCount,
                incoming.CommentCount,
                incoming.ShareCount,
                incoming.SummaryVersion,
                incoming.SourceOccurredAtUtc,
                observedAtUtc);

        private static ValidSummary Validate(EngagementSummaryProjectionDto summary)
        {
            ArgumentNullException.ThrowIfNull(summary);
            if (summary.EventId == Guid.Empty)
                throw new ArgumentException("An engagement event ID is required.", nameof(summary));
            if (summary.SummaryVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(summary.SummaryVersion));
            FeedEngagementSummary.ValidateCounters(
                summary.LikeCount,
                summary.DislikeCount,
                summary.RatingCount,
                summary.RatingTotal,
                summary.ViewCount,
                summary.CommentCount,
                summary.ShareCount);
            return new ValidSummary(
                summary.EventId,
                FeedEngagementResourceTypes.Normalize(summary.ResourceType),
                FeedEngagementResourceTypes.NormalizeId(summary.ResourceId),
                summary.LikeCount,
                summary.DislikeCount,
                summary.RatingCount,
                summary.RatingTotal,
                summary.ViewCount,
                summary.CommentCount,
                summary.ShareCount,
                summary.SummaryVersion,
                NormalizeUtc(summary.SourceOccurredAtUtc, nameof(summary.SourceOccurredAtUtc)));
        }

        private static bool SameResource(
            FeedEngagementSummaryEventQuarantine item,
            ValidSummary incoming) =>
            item.ResourceType == incoming.ResourceType
            && item.ResourceId == incoming.ResourceId;

        private static DateTime NormalizeUtc(DateTime value, string parameterName)
        {
            if (value == default)
                throw new ArgumentException("A source timestamp is required.", parameterName);
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }

        private sealed record ValidSummary(
            Guid EventId,
            string ResourceType,
            string ResourceId,
            long LikeCount,
            long DislikeCount,
            long RatingCount,
            long RatingTotal,
            long ViewCount,
            long CommentCount,
            long ShareCount,
            long SummaryVersion,
            DateTime SourceOccurredAtUtc);
    }
}
