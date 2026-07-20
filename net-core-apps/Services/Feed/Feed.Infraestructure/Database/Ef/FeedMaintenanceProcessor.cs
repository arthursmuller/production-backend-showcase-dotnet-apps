using Feed.Application.Features.Feeds;
using Feed.Application.Features.Privacy;
using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    /// <summary>
    /// Bounded, checkpointed retention and replay work. Every page commits its data mutation
    /// and cursor together so a crash can only repeat an idempotent page.
    /// </summary>
    public sealed class FeedMaintenanceProcessor(
        FeedContext context,
        IFeedUserErasureAuthority erasureAuthority,
        IOptions<FeedOptions> options,
        FeedTelemetry telemetry) : IFeedMaintenanceProcessor
    {
        private readonly FeedOptions _options = options.Value;

        public async Task<int> CompactAsync(
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var observed = NormalizeUtc(observedAtUtc);
            var entryCutoff = observed.AddDays(-_options.EntryRetentionDays);
            var workCutoff = observed.AddDays(-_options.CompletedWorkRetentionDays);
            var compacted = 0;

            compacted += await CompactPageAsync(
                "entry-retention",
                checkpoint => context.FeedEntries
                    .Where(entry => entry.Id > checkpoint.Cursor
                        && entry.PublishedAtUtc < entryCutoff)
                    .Select(entry => entry.Id),
                ids => context.FeedEntries
                    .Where(entry => ids.Contains(entry.Id))
                    .ExecuteDeleteAsync(cancellationToken),
                observed,
                cancellationToken);
            compacted += await CompactPageAsync(
                "fanout-retention",
                checkpoint => context.FanoutWorkItems
                    .Where(work => work.Id > checkpoint.Cursor
                        && (work.State == FeedWorkStates.Completed
                            || work.State == FeedWorkStates.Failed)
                        && work.UpdatedAtUtc < workCutoff)
                    .Select(work => work.Id),
                ids => context.FanoutWorkItems
                    .Where(work => ids.Contains(work.Id))
                    .ExecuteDeleteAsync(cancellationToken),
                observed,
                cancellationToken);
            compacted += await CompactPageAsync(
                "revocation-retention",
                checkpoint => context.FeedRevocationWorkItems
                    .Where(work => work.Id > checkpoint.Cursor
                        && (work.State == FeedWorkStates.Completed
                            || work.State == FeedWorkStates.Failed)
                        && work.UpdatedAtUtc < workCutoff)
                    .Select(work => work.Id),
                ids => context.FeedRevocationWorkItems
                    .Where(work => ids.Contains(work.Id))
                    .ExecuteDeleteAsync(cancellationToken),
                observed,
                cancellationToken);
            compacted += await CompactPageAsync(
                "relationship-quarantine",
                checkpoint => context.FeedRelationshipEventQuarantines
                    .Where(item => item.Id > checkpoint.Cursor
                        && item.ResolvedAtUtc != null
                        && item.ResolvedAtUtc < workCutoff)
                    .Select(item => item.Id),
                ids => context.FeedRelationshipEventQuarantines
                    .Where(item => ids.Contains(item.Id))
                    .ExecuteDeleteAsync(cancellationToken),
                observed,
                cancellationToken);
            compacted += await CompactPageAsync(
                "quarantine-retention",
                checkpoint => context.FeedActivityEventQuarantines
                    .Where(item => item.Id > checkpoint.Cursor
                        && item.ResolvedAtUtc != null
                        && item.ResolvedAtUtc < workCutoff)
                    .Select(item => item.Id),
                ids => context.FeedActivityEventQuarantines
                    .Where(item => ids.Contains(item.Id))
                    .ExecuteDeleteAsync(cancellationToken),
                observed,
                cancellationToken);
            compacted += await CompactPageAsync(
                "activity-retention",
                checkpoint => context.FeedActivities
                    .Where(activity => activity.Id > checkpoint.Cursor
                        && activity.IsTombstoned
                        && activity.UpdatedAtUtc < entryCutoff
                        && !context.FeedEntries.Any(entry => entry.ActivityId == activity.Id)
                        && !context.FanoutWorkItems.Any(work => work.ActivityId == activity.Id)
                        && !context.FeedRevocationWorkItems.Any(work => work.ActivityId == activity.Id))
                    .Select(activity => activity.Id),
                ids => context.FeedActivities
                    .Where(activity => ids.Contains(activity.Id))
                    .ExecuteDeleteAsync(cancellationToken),
                observed,
                cancellationToken);

            telemetry.RecordBatch("maintenance", "compaction", compacted);
            return compacted;
        }

        public async Task<int> ReplayAsync(
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var observed = NormalizeUtc(observedAtUtc);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var checkpoint = await GetCheckpointAsync(
                "replay-rebuild",
                observed,
                cancellationToken);
            var scannedActivities = await context.FeedActivities
                .Where(activity => activity.Id > checkpoint.Cursor
                    && !activity.IsTombstoned
                    && activity.DeliveryMode == FeedDeliveryModes.Push)
                .OrderBy(activity => activity.Id)
                .Take(_options.FanoutBatchSize)
                .ToListAsync(cancellationToken);
            var actors = scannedActivities.Select(activity => activity.ActorUserId).Distinct().ToArray();
            await using var mutation = actors.Length == 0
                ? null
                : await erasureAuthority.AcquireMutationAsync(actors, cancellationToken);
            var activities = mutation is null || mutation.Allowed
                ? scannedActivities
                : scannedActivities.Where(activity =>
                        !mutation.ErasedUserIds.Contains(activity.ActorUserId))
                    .ToList();
            var activityIds = activities.Select(activity => activity.Id).ToArray();
            var existingWork = await context.FanoutWorkItems
                .Where(work => activityIds.Contains(work.ActivityId))
                .Select(work => new { work.ActivityId, work.SourceEventId, work.State })
                .ToListAsync(cancellationToken);
            var protectedActivities = existingWork
                .Where(work => work.State != FeedWorkStates.Failed)
                .Select(work => work.ActivityId)
                .ToHashSet();
            var existingSources = existingWork
                .Select(work => work.SourceEventId)
                .Concat(context.FanoutWorkItems.Local.Select(work => work.SourceEventId))
                .ToHashSet(StringComparer.Ordinal);
            var scheduled = 0;
            foreach (var activity in activities)
            {
                var sourceEventId = $"replay:{activity.Id}:{activity.SourceVersion}";
                if (protectedActivities.Contains(activity.Id)
                    || !existingSources.Add(sourceEventId))
                    continue;
                await context.FanoutWorkItems.AddAsync(
                    FanoutWorkItem.Create(
                        sourceEventId,
                        activity,
                        observed,
                        FeedWorkKinds.Replay),
                    cancellationToken);
                scheduled++;
            }

            if (scannedActivities.Count < _options.FanoutBatchSize)
                checkpoint.Restart(observed);
            else
                checkpoint.Advance(scannedActivities[^1].Id, observed);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            telemetry.RecordBatch("maintenance", "replay", scheduled);
            return scheduled;
        }

        private async Task<int> CompactPageAsync(
            string jobName,
            Func<FeedMaintenanceCheckpoint, IQueryable<long>> selectIds,
            Func<long[], Task<int>> delete,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var checkpoint = await GetCheckpointAsync(jobName, observedAtUtc, cancellationToken);
            var ids = await selectIds(checkpoint)
                .OrderBy(id => id)
                .Take(_options.FanoutBatchSize)
                .ToArrayAsync(cancellationToken);
            var deleted = ids.Length == 0 ? 0 : await delete(ids);
            if (ids.Length < _options.FanoutBatchSize)
                checkpoint.Restart(observedAtUtc);
            else
                checkpoint.Advance(ids[^1], observedAtUtc);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }

        private async Task<FeedMaintenanceCheckpoint> GetCheckpointAsync(
            string jobName,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var local = context.FeedMaintenanceCheckpoints.Local.SingleOrDefault(item =>
                item.JobName == jobName);
            if (local is not null)
                return local;
            var checkpoint = await context.FeedMaintenanceCheckpoints.SingleOrDefaultAsync(
                item => item.JobName == jobName,
                cancellationToken);
            if (checkpoint is not null)
                return checkpoint;
            checkpoint = FeedMaintenanceCheckpoint.Create(jobName, observedAtUtc);
            await context.FeedMaintenanceCheckpoints.AddAsync(checkpoint, cancellationToken);
            return checkpoint;
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
    }
}
