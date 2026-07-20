using Feed.Application.Features.Privacy;
using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Providers;
using MessageBrokerContracts.Following;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence;

public sealed class FeedUserErasureProcessor(
    FeedContext context,
    FeedUserErasureAuthority authority,
    IIntegrationMessagePublisher publisher,
    TimeProvider timeProvider,
    IOptions<FeedUserErasureOptions> configuredOptions,
    IOptions<FeedOptions> configuredFeedOptions,
    FeedUserErasureMetrics metrics) : IFeedUserErasureProcessor
{
    private readonly FeedUserErasureOptions _options = configuredOptions.Value;
    private readonly FeedOptions _feedOptions = configuredFeedOptions.Value;

    public async Task<FeedUserErasureCycleResult> ProcessCycleAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        ValidateWorker(workerId);
        var claim = await ClaimAsync(workerId, cancellationToken);
        if (claim is null)
            return new(false, false, false, 0, 0);

        metrics.Claimed();
        var batches = 0;
        var rows = 0;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.CycleTimeBudget);
        try
        {
            while (batches < _options.MaximumBatchesPerCycle
                && rows + _options.BatchSize <= _options.MaximumRowsPerCycle)
            {
                var result = await ProcessBatchAsync(
                    claim.UserId,
                    claim.ErasureVersion,
                    workerId,
                    claim.LeaseToken,
                    budget.Token);
                if (result.LeaseLost)
                {
                    metrics.LeaseLost();
                    return new(true, false, true, batches, rows);
                }

                batches++;
                rows += result.Rows;
                metrics.Rows(result.Rows);
                if (result.Completed)
                {
                    metrics.Completed();
                    return new(true, true, false, batches, rows);
                }
                if (result.Waiting)
                    break;
            }

            await ReleaseAsync(
                claim.UserId,
                claim.ErasureVersion,
                workerId,
                claim.LeaseToken,
                cancellationToken);
            return new(true, false, false, batches, rows);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            return new(true, false, false, batches, rows);
        }
        catch
        {
            metrics.Failure();
            throw;
        }
    }

    private async Task<FeedUserErasure?> ClaimAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var now = Now();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var job = await context.FeedUserErasures
            .FromSqlInterpolated($"""
                SELECT * FROM `FeedUserErasure`
                WHERE `Status` <> {(byte)FeedUserErasureStatus.Completed}
                  AND (`LeaseExpiresAtUtc` IS NULL OR `LeaseExpiresAtUtc` <= {now})
                ORDER BY `RequestedAtUtc`, `UserId`
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.Claim(workerId, now.Add(_options.LeaseDuration), now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    private async Task<BatchResult> ProcessBatchAsync(
        int userId,
        long erasureVersion,
        string workerId,
        long leaseToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var now = Now();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            var job = await context.FeedUserErasures
                .FromSqlInterpolated($"""
                    SELECT * FROM `FeedUserErasure`
                    WHERE `UserId` = {userId}
                    FOR UPDATE
                    """)
                .SingleAsync(cancellationToken);
            if (job.ErasureVersion != erasureVersion
                || !job.OwnsLease(workerId, leaseToken, now))
            {
                using var rollbackTimeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));
                await transaction.RollbackAsync(rollbackTimeout.Token);
                return BatchResult.Lost;
            }

            if (job.Stage == FeedUserErasureStage.Complete)
            {
                var eventId = FeedUserErasureEventIds.Create(
                    job.RequestId,
                    job.ErasureVersion,
                    UserErasureOutcomes.Completed);
                await context.PublishToBusOutboxAsync(
                    publisher,
                    new UserErasureCompleted
                    {
                        EventId = eventId,
                        RequestId = job.RequestId,
                        UserId = job.UserId,
                        ErasureVersion = job.ErasureVersion,
                        Service = "feed",
                        Outcome = UserErasureOutcomes.Completed,
                        CompletedAtUtc = new DateTimeOffset(now, TimeSpan.Zero),
                    },
                    eventId,
                    cancellationToken);
                job.Complete(eventId, workerId, leaseToken, now);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(0, true, false, false);
            }

            var page = await ProcessStageAsync(job, now, cancellationToken);
            job.Advance(
                page.NextStage,
                page.NextCursor,
                page.NextSecondaryCursor,
                workerId,
                leaseToken,
                now.Add(_options.LeaseDuration),
                now,
                page.CacheSafeAfterUtc);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(page.Rows, false, false, page.Waiting);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Rollback must outlive a canceled request, while remaining bounded.
            using var rollbackTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(rollbackTimeout.Token);
            context.ChangeTracker.Clear();
            return BatchResult.Lost;
        }
    }

    private Task<StagePage> ProcessStageAsync(
        FeedUserErasure job,
        DateTime now,
        CancellationToken cancellationToken) => job.Stage switch
        {
            FeedUserErasureStage.PublisherFanoutWork => DeleteLongPageAsync(
                job,
                context.FanoutWorkItems.Where(item => item.PublisherUserId == job.UserId),
                FeedUserErasureStage.PublisherRevocationWork,
                cancellationToken),
            FeedUserErasureStage.PublisherRevocationWork => DeleteLongPageAsync(
                job,
                context.FeedRevocationWorkItems.Where(item => item.OwnerUserId == job.UserId),
                FeedUserErasureStage.PublisherEntries,
                cancellationToken),
            FeedUserErasureStage.PublisherEntries =>
                DeletePublisherEntriesAsync(job, now, cancellationToken),
            FeedUserErasureStage.PublisherActivities => DeleteLongPageAsync(
                job,
                context.FeedActivities.Where(item => item.ActorUserId == job.UserId),
                FeedUserErasureStage.PublisherActivityQuarantine,
                cancellationToken),
            FeedUserErasureStage.PublisherActivityQuarantine => DeleteLongPageAsync(
                job,
                context.FeedActivityEventQuarantines.Where(item => item.ActorUserId == job.UserId),
                FeedUserErasureStage.PublisherState,
                cancellationToken),
            FeedUserErasureStage.PublisherState =>
                DeletePublisherStateAsync(job, cancellationToken),
            FeedUserErasureStage.ReaderEntries =>
                DeleteReaderEntriesAsync(job, cancellationToken),
            FeedUserErasureStage.ReaderState =>
                DeleteReaderStateAsync(job, cancellationToken),
            FeedUserErasureStage.RevocationsAsOwner => DeleteLongPageAsync(
                job,
                context.FeedRevocationWorkItems.Where(item => item.OwnerUserId == job.UserId),
                FeedUserErasureStage.RevocationsAsViewer,
                cancellationToken),
            FeedUserErasureStage.RevocationsAsViewer => DeleteLongPageAsync(
                job,
                context.FeedRevocationWorkItems.Where(item => item.ViewerUserId == job.UserId),
                FeedUserErasureStage.RelationshipQuarantineAsOwner,
                cancellationToken),
            FeedUserErasureStage.RelationshipQuarantineAsOwner => DeleteLongPageAsync(
                job,
                context.FeedRelationshipEventQuarantines.Where(item => item.OwnerUserId == job.UserId),
                FeedUserErasureStage.RelationshipQuarantineAsViewer,
                cancellationToken),
            FeedUserErasureStage.RelationshipQuarantineAsViewer => DeleteLongPageAsync(
                job,
                context.FeedRelationshipEventQuarantines.Where(item => item.ViewerUserId == job.UserId),
                FeedUserErasureStage.AudienceEdgesAsOwner,
                cancellationToken),
            FeedUserErasureStage.AudienceEdgesAsOwner =>
                DeleteAudienceEdgesAsync(job, asOwner: true, now, cancellationToken),
            FeedUserErasureStage.AudienceEdgesAsViewer =>
                DeleteAudienceEdgesAsync(job, asOwner: false, now, cancellationToken),
            FeedUserErasureStage.RelationshipCheckpointsAsOwner =>
                DeleteCheckpointPageAsync(job, asOwner: true, cancellationToken),
            FeedUserErasureStage.RelationshipCheckpointsAsViewer =>
                DeleteCheckpointPageAsync(job, asOwner: false, cancellationToken),
            FeedUserErasureStage.CacheExpiryBarrier =>
                ProcessCacheBarrier(job, now),
            _ => throw new InvalidOperationException("Unknown Feed erasure stage."),
        };

    private async Task<StagePage> DeletePublisherEntriesAsync(
        FeedUserErasure job,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activityId = job.SecondaryCursor > 0
            ? job.Cursor
            : await context.FeedActivities.AsNoTracking()
                .Where(item => item.ActorUserId == job.UserId && item.Id > job.Cursor)
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (activityId == 0)
            return StagePage.AdvanceTo(FeedUserErasureStage.PublisherActivities);

        var entryCursor = job.SecondaryCursor;
        var rows = await context.FeedEntries
            .Where(item => item.ActivityId == activityId && item.Id > entryCursor)
            .OrderBy(item => item.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return new(job.Stage, activityId, 0, 0);

        var recipients = rows.Select(item => item.RecipientUserId).Distinct().ToArray();
        await using var mutation = await authority.AcquireMutationAsync(recipients, cancellationToken);
        context.FeedEntries.RemoveRange(rows);
        await BumpRevisionsAsync(
            recipients.Where(userId => !mutation.ErasedUserIds.Contains(userId)).ToArray(),
            now,
            cancellationToken);
        await mutation.CommitAsync(cancellationToken);
        var completeParent = rows.Count < _options.BatchSize;
        return new(
            job.Stage,
            activityId,
            completeParent ? 0 : rows[^1].Id,
            rows.Count);
    }

    private async Task<StagePage> DeleteReaderEntriesAsync(
        FeedUserErasure job,
        CancellationToken cancellationToken)
    {
        var rows = await context.FeedEntries
            .Where(item => item.RecipientUserId == job.UserId && item.Id > job.Cursor)
            .OrderBy(item => item.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return StagePage.AdvanceTo(FeedUserErasureStage.ReaderState);
        context.FeedEntries.RemoveRange(rows);
        return new(job.Stage, rows[^1].Id, 0, rows.Count);
    }

    private async Task<StagePage> DeletePublisherStateAsync(
        FeedUserErasure job,
        CancellationToken cancellationToken)
    {
        var row = await context.FeedPublisherStates.SingleOrDefaultAsync(
            item => item.PublisherUserId == job.UserId,
            cancellationToken);
        if (row is not null)
            context.FeedPublisherStates.Remove(row);
        return new(FeedUserErasureStage.ReaderEntries, 0, 0, row is null ? 0 : 1);
    }

    private async Task<StagePage> DeleteReaderStateAsync(
        FeedUserErasure job,
        CancellationToken cancellationToken)
    {
        var row = await context.FeedUserStates.SingleOrDefaultAsync(
            item => item.UserId == job.UserId,
            cancellationToken);
        if (row is not null)
            context.FeedUserStates.Remove(row);
        return new(FeedUserErasureStage.RevocationsAsOwner, 0, 0, row is null ? 0 : 1);
    }

    private async Task<StagePage> DeleteAudienceEdgesAsync(
        FeedUserErasure job,
        bool asOwner,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var query = context.FeedAudienceEdges.Where(item => item.Id > job.Cursor);
        query = asOwner
            ? query.Where(item => item.OwnerUserId == job.UserId)
            : query.Where(item => item.ViewerUserId == job.UserId);
        var rows = await query.OrderBy(item => item.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return StagePage.AdvanceTo(asOwner
                ? FeedUserErasureStage.AudienceEdgesAsViewer
                : FeedUserErasureStage.RelationshipCheckpointsAsOwner);
        }

        int[] publishers = asOwner
            ? []
            : rows.Select(item => item.OwnerUserId).Where(id => id != job.UserId).Distinct().ToArray();
        context.FeedAudienceEdges.RemoveRange(rows);
        if (publishers.Length > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            await RefreshPublisherAudienceCountsAsync(publishers, now, cancellationToken);
        }
        return new(job.Stage, rows[^1].Id, 0, rows.Count);
    }

    private async Task<StagePage> DeleteCheckpointPageAsync(
        FeedUserErasure job,
        bool asOwner,
        CancellationToken cancellationToken)
    {
        var query = context.FeedRelationshipCheckpoints.AsQueryable();
        query = asOwner
            ? query.Where(item => item.OwnerUserId == job.UserId)
            : query.Where(item => item.ViewerUserId == job.UserId);
        var rows = await query.OrderBy(item => item.StreamId)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return StagePage.AdvanceTo(asOwner
                ? FeedUserErasureStage.RelationshipCheckpointsAsViewer
                : FeedUserErasureStage.CacheExpiryBarrier);
        }
        context.FeedRelationshipCheckpoints.RemoveRange(rows);
        return new(job.Stage, 0, 0, rows.Count);
    }

    private Task<StagePage> DeleteLongPageAsync<T>(
        FeedUserErasure job,
        IQueryable<T> query,
        FeedUserErasureStage nextStage,
        CancellationToken cancellationToken)
        where T : class => DeleteLongPageCoreAsync(job, query, nextStage, cancellationToken);

    private async Task<StagePage> DeleteLongPageCoreAsync<T>(
        FeedUserErasure job,
        IQueryable<T> query,
        FeedUserErasureStage nextStage,
        CancellationToken cancellationToken)
        where T : class
    {
        var rows = await query
            .Where(item => EF.Property<long>(item, "Id") > job.Cursor)
            .OrderBy(item => EF.Property<long>(item, "Id"))
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return StagePage.AdvanceTo(nextStage);
        context.RemoveRange(rows);
        return new(
            job.Stage,
            context.Entry(rows[^1]).Property<long>("Id").CurrentValue,
            0,
            rows.Count);
    }

    private async Task BumpRevisionsAsync(
        IReadOnlyCollection<int> userIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return;
        var ids = userIds.ToArray();
        var states = await context.FeedUserStates
            .Where(item => ids.Contains(item.UserId))
            .ToListAsync(cancellationToken);
        var byId = states.ToDictionary(item => item.UserId);
        foreach (var userId in ids)
        {
            if (!byId.TryGetValue(userId, out var state))
            {
                state = FeedUserState.Create(userId, now);
                await context.FeedUserStates.AddAsync(state, cancellationToken);
            }
            state.IncrementRevision(now);
        }
    }

    private async Task RefreshPublisherAudienceCountsAsync(
        IReadOnlyCollection<int> publisherIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var erased = await authority.GetErasedAsync(publisherIds, cancellationToken);
        var ids = publisherIds.Where(id => !erased.Contains(id)).ToArray();
        if (ids.Length == 0)
            return;
        var states = await context.FeedPublisherStates
            .Where(item => ids.Contains(item.PublisherUserId))
            .ToListAsync(cancellationToken);
        var stateIds = states.Select(item => item.PublisherUserId).ToArray();
        if (stateIds.Length == 0)
            return;
        var counts = await context.FeedAudienceEdges
            .Where(edge => stateIds.Contains(edge.OwnerUserId)
                && edge.State == RelationshipStates.Active
                && (edge.RelationshipKind == RelationshipKinds.Follow
                    || edge.RelationshipKind == RelationshipKinds.Friend))
            .Join(
                context.FeedRelationshipCheckpoints.Where(checkpoint =>
                    checkpoint.Status == AudienceProjectionStates.Healthy),
                edge => edge.SourceStreamId,
                checkpoint => (Guid?)checkpoint.StreamId,
                (edge, checkpoint) => new { edge, checkpoint })
            .Where(pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion)
            .Select(pair => new { pair.edge.OwnerUserId, pair.edge.ViewerUserId })
            .Distinct()
            .GroupBy(item => item.OwnerUserId)
            .Select(group => new { PublisherUserId = group.Key, Count = group.LongCount() })
            .ToDictionaryAsync(item => item.PublisherUserId, item => item.Count, cancellationToken);
        foreach (var state in states)
        {
            state.ObserveAudienceCount(
                counts.GetValueOrDefault(state.PublisherUserId),
                _feedOptions.PullEnterAudienceCount,
                _feedOptions.PullExitAudienceCount,
                now);
        }
    }

    private Task<StagePage> ProcessCacheBarrier(FeedUserErasure job, DateTime now)
    {
        if (!job.CacheSafeAfterUtc.HasValue)
        {
            var safeAfter = now.AddSeconds(
                _feedOptions.FirstPageCacheSeconds + _options.CacheExpirySafetySeconds);
            return Task.FromResult(new StagePage(
                job.Stage,
                0,
                0,
                0,
                Waiting: true,
                CacheSafeAfterUtc: safeAfter));
        }
        return Task.FromResult(job.CacheSafeAfterUtc > now
            ? new StagePage(job.Stage, 0, 0, 0, Waiting: true)
            : StagePage.AdvanceTo(FeedUserErasureStage.Complete));
    }

    private async Task ReleaseAsync(
        int userId,
        long erasureVersion,
        string workerId,
        long leaseToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var job = await context.FeedUserErasures.SingleAsync(
            item => item.UserId == userId,
            cancellationToken);
        var now = Now();
        if (job.ErasureVersion != erasureVersion || !job.OwnsLease(workerId, leaseToken, now))
            return;
        job.Release(workerId, leaseToken, now);
        await context.SaveChangesAsync(cancellationToken);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static void ValidateWorker(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128)
            throw new ArgumentException("A bounded worker ID is required.", nameof(workerId));
    }

    private sealed record StagePage(
        FeedUserErasureStage NextStage,
        long NextCursor,
        long NextSecondaryCursor,
        int Rows,
        bool Waiting = false,
        DateTime? CacheSafeAfterUtc = null)
    {
        public static StagePage AdvanceTo(FeedUserErasureStage stage) => new(stage, 0, 0, 0);
    }

    private sealed record BatchResult(int Rows, bool Completed, bool LeaseLost, bool Waiting)
    {
        public static BatchResult Lost { get; } = new(0, false, true, false);
    }
}
