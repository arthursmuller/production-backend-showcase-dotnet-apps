using Following.Application.Providers.Categories;
using Following.Application.Providers.Privacy;
using Following.Application.Providers.Relationships;
using Following.Application.Providers.Time;
using Following.Domain.AggregatesModel;
using Following.Infra.Providers;
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

namespace Following.Infra.Persistence
{
    public sealed class FollowingUserErasureProcessor(
        FollowingContext context,
        IRelationshipEventWriter relationshipEvents,
        Following.Application.Providers.Categories.CategoriesProvider categories,
        IIntegrationMessagePublisher publisher,
        IClock clock,
        IOptions<FollowingUserErasureOptions> configuredOptions,
        FollowingUserErasureMetrics metrics) : IFollowingUserErasureProcessor
    {
        private readonly FollowingUserErasureOptions _options = configuredOptions.Value;

        public async Task<FollowingUserErasureCycleResult> ProcessCycleAsync(
            string workerId,
            CancellationToken cancellationToken)
        {
            ValidateWorker(workerId);
            var claim = await ClaimAsync(workerId, cancellationToken);
            if (claim is null)
                return new FollowingUserErasureCycleResult(false, false, 0, 0, false);

            metrics.Claimed();
            var batches = 0;
            var rows = 0;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(_options.CycleTimeBudget);

            try
            {
                while (batches < _options.MaxBatchesPerCycle)
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
                        return new FollowingUserErasureCycleResult(true, false, batches, rows, true);
                    }

                    batches++;
                    rows += result.Rows;
                    metrics.Rows(result.Rows);
                    if (result.Completed)
                    {
                        metrics.Completed();
                        return new FollowingUserErasureCycleResult(true, true, batches, rows, false);
                    }
                }

                await ReleaseAsync(
                    claim.UserId,
                    claim.ErasureVersion,
                    workerId,
                    claim.LeaseToken,
                    cancellationToken);
                return new FollowingUserErasureCycleResult(true, false, batches, rows, false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            {
                // The persisted lease bounds recovery if the hard cycle budget interrupts a DB call.
                return new FollowingUserErasureCycleResult(true, false, batches, rows, false);
            }
            catch
            {
                metrics.Failure();
                throw;
            }
        }

        private async Task<FollowingUserErasure?> ClaimAsync(
            string workerId,
            CancellationToken cancellationToken)
        {
            context.ChangeTracker.Clear();
            var nowUtc = NormalizeUtc(clock.UtcNow);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var job = await context.UserErasures
                .FromSqlInterpolated($"""
                    SELECT * FROM `FollowingUserErasure`
                    WHERE `Status` <> {(byte)FollowingUserErasureStatus.Completed}
                      AND (`LeaseExpiresAtUtc` IS NULL OR `LeaseExpiresAtUtc` <= {nowUtc})
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

            job.Claim(workerId, nowUtc.Add(_options.LeaseDuration), nowUtc);
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
            var job = await context.UserErasures
                .IgnoreQueryFilters()
                .SingleAsync(item => item.UserId == userId, cancellationToken);
            var nowUtc = NormalizeUtc(clock.UtcNow);
            if (job.ErasureVersion != erasureVersion
                || !job.OwnsLease(workerId, leaseToken, nowUtc))
            {
                return BatchResult.Lost;
            }

            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            try
            {
                if (job.Stage == FollowingUserErasureStage.Complete)
                {
                    var eventId = FollowingUserErasureEventIds.Create(
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
                            Service = "following",
                            Outcome = UserErasureOutcomes.Completed,
                            CompletedAtUtc = new DateTimeOffset(nowUtc, TimeSpan.Zero),
                        },
                        eventId,
                        cancellationToken);
                    job.Complete(eventId, workerId, leaseToken, nowUtc);
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new BatchResult(0, true, false);
                }

                var page = await ProcessStageAsync(job, cancellationToken);
                job.Advance(
                    page.NextStage,
                    page.NextCursor,
                    workerId,
                    leaseToken,
                    nowUtc.Add(_options.LeaseDuration),
                    nowUtc);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new BatchResult(page.Rows, false, false);
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
            FollowingUserErasure job,
            CancellationToken cancellationToken) => job.Stage switch
            {
                FollowingUserErasureStage.FollowsAsFollower =>
                    ProcessFollowsAsync(job, asFollower: true, cancellationToken),
                FollowingUserErasureStage.FollowsAsFollowed =>
                    ProcessFollowsAsync(job, asFollower: false, cancellationToken),
                FollowingUserErasureStage.FriendshipsAsOwner =>
                    ProcessFriendshipsAsync(job, asOwner: true, cancellationToken),
                FollowingUserErasureStage.FriendshipsAsFriend =>
                    ProcessFriendshipsAsync(job, asOwner: false, cancellationToken),
                FollowingUserErasureStage.FriendRequestsAsSender =>
                    ProcessFriendRequestsAsync(job, asSender: true, cancellationToken),
                FollowingUserErasureStage.FriendRequestsAsRecipient =>
                    ProcessFriendRequestsAsync(job, asSender: false, cancellationToken),
                FollowingUserErasureStage.BlocksAsBlocker =>
                    ProcessBlocksAsync(job, asBlocker: true, cancellationToken),
                FollowingUserErasureStage.BlocksAsBlocked =>
                    ProcessBlocksAsync(job, asBlocker: false, cancellationToken),
                FollowingUserErasureStage.SubscriptionsAsOwner =>
                    ProcessSubscriptionsAsync(job, asOwner: true, cancellationToken),
                FollowingUserErasureStage.SubscriptionsAsRequester =>
                    ProcessSubscriptionsAsync(job, asOwner: false, cancellationToken),
                FollowingUserErasureStage.CategoryFollows =>
                    ProcessCategoryFollowsAsync(job, cancellationToken),
                FollowingUserErasureStage.RelationshipStreamsAsOwner =>
                    ProcessStreamsAsync(job, asOwner: true, cancellationToken),
                FollowingUserErasureStage.RelationshipStreamsAsViewer =>
                    ProcessStreamsAsync(job, asOwner: false, cancellationToken),
                FollowingUserErasureStage.Analytics =>
                    ProcessAnalyticsAsync(job, cancellationToken),
                _ => throw new InvalidOperationException("Unknown Following erasure stage."),
            };

        private async Task<StagePage> ProcessFollowsAsync(
            FollowingUserErasure job,
            bool asFollower,
            CancellationToken cancellationToken)
        {
            var query = context.Follows.IgnoreQueryFilters().Where(item => item.Id > job.Cursor);
            query = asFollower
                ? query.Where(item => item.FollowerUserId == job.UserId)
                : query.Where(item => item.FollowedUserId == job.UserId);
            var rows = await PageAsync(query, cancellationToken);
            foreach (var row in rows)
            {
                if (asFollower)
                {
                    await DecrementAnalyticsAsync(
                        row.FollowedUserId,
                        job.UserId,
                        AnalyticsCounter.Followers,
                        cancellationToken);
                }
                else
                {
                    await DecrementAnalyticsAsync(
                        row.FollowerUserId,
                        job.UserId,
                        AnalyticsCounter.Following,
                        cancellationToken);
                }

                await StageRevocationAsync(
                    row.FollowedUserId,
                    row.FollowerUserId,
                    RelationshipKinds.Follow,
                    cancellationToken);
            }
            context.Follows.RemoveRange(rows);
            return Advance(job, rows);
        }

        private async Task<StagePage> ProcessFriendshipsAsync(
            FollowingUserErasure job,
            bool asOwner,
            CancellationToken cancellationToken)
        {
            var query = context.Friendships.IgnoreQueryFilters().Where(item => item.Id > job.Cursor);
            query = asOwner
                ? query.Where(item => item.UserId == job.UserId)
                : query.Where(item => item.FriendUserId == job.UserId);
            var rows = await PageAsync(query, cancellationToken);
            foreach (var row in rows)
            {
                if (!asOwner)
                {
                    await DecrementAnalyticsAsync(
                        row.UserId,
                        job.UserId,
                        AnalyticsCounter.Friends,
                        cancellationToken);
                }
                await StageRevocationAsync(
                    row.UserId,
                    row.FriendUserId,
                    RelationshipKinds.Friend,
                    cancellationToken);
            }
            context.Friendships.RemoveRange(rows);
            return Advance(job, rows);
        }

        private async Task<StagePage> ProcessFriendRequestsAsync(
            FollowingUserErasure job,
            bool asSender,
            CancellationToken cancellationToken)
        {
            var query = context.FriendRequests.IgnoreQueryFilters().Where(item => item.Id > job.Cursor);
            query = asSender
                ? query.Where(item => item.SenderUserId == job.UserId)
                : query.Where(item => item.RecipientUserId == job.UserId);
            var rows = await PageAsync(query, cancellationToken);
            foreach (var row in rows)
            {
                if (asSender && row.Status == FriendRequestStatus.Pending)
                {
                    await DecrementAnalyticsAsync(
                        row.RecipientUserId,
                        job.UserId,
                        AnalyticsCounter.FriendRequests,
                        cancellationToken);
                }
                await StageRevocationAsync(
                    row.SenderUserId,
                    row.RecipientUserId,
                    RelationshipKinds.Friend,
                    cancellationToken);
                await StageRevocationAsync(
                    row.RecipientUserId,
                    row.SenderUserId,
                    RelationshipKinds.Friend,
                    cancellationToken);
            }
            context.FriendRequests.RemoveRange(rows);
            return Advance(job, rows);
        }

        private async Task<StagePage> ProcessBlocksAsync(
            FollowingUserErasure job,
            bool asBlocker,
            CancellationToken cancellationToken)
        {
            var query = context.UserBlocks.IgnoreQueryFilters().Where(item => item.Id > job.Cursor);
            query = asBlocker
                ? query.Where(item => item.BlockerUserId == job.UserId)
                : query.Where(item => item.BlockedUserId == job.UserId);
            var rows = await PageAsync(query, cancellationToken);
            foreach (var row in rows)
            {
                await StageRevocationAsync(
                    row.BlockerUserId,
                    row.BlockedUserId,
                    RelationshipKinds.Block,
                    cancellationToken);
            }
            context.UserBlocks.RemoveRange(rows);
            return Advance(job, rows);
        }

        private async Task<StagePage> ProcessSubscriptionsAsync(
            FollowingUserErasure job,
            bool asOwner,
            CancellationToken cancellationToken)
        {
            var query = context.FriendsSubscriptionsFeature
                .IgnoreQueryFilters()
                .Where(item => item.Id > job.Cursor);
            query = asOwner
                ? query.Where(item => item.OwnerId == job.UserId)
                : query.Where(item => item.RequesterId == job.UserId);
            var rows = await PageAsync(query, cancellationToken);
            foreach (var row in rows)
            {
                await StageRevocationAsync(
                    row.OwnerId,
                    row.RequesterId,
                    RelationshipKinds.Friend,
                    cancellationToken);
                await StageRevocationAsync(
                    row.RequesterId,
                    row.OwnerId,
                    RelationshipKinds.Friend,
                    cancellationToken);
            }
            context.FriendsSubscriptionsFeature.RemoveRange(rows);
            return Advance(job, rows);
        }

        private async Task<StagePage> ProcessCategoryFollowsAsync(
            FollowingUserErasure job,
            CancellationToken cancellationToken)
        {
            var rows = await PageAsync(
                context.CategoryFollows
                    .IgnoreQueryFilters()
                    .Where(item => item.UserId == job.UserId && item.Id > job.Cursor),
                cancellationToken);
            if (rows.Count > 0)
            {
                var categoryNames = rows
                    .Select(item => item.CategoryName)
                    .OfType<string>()
                    .Where(name => name.Length > 0)
                    .ToArray();
                await categories.UpdateFollowAsync(
                    rows.Select(item => item.CategoryId).ToArray(),
                    categoryNames,
                    false,
                    cancellationToken);
                context.CategoryFollows.RemoveRange(rows);
            }
            return Advance(job, rows);
        }

        private async Task<StagePage> ProcessStreamsAsync(
            FollowingUserErasure job,
            bool asOwner,
            CancellationToken cancellationToken)
        {
            var query = context.RelationshipEventStreams
                .IgnoreQueryFilters()
                .Where(item => item.Id > job.Cursor);
            query = asOwner
                ? query.Where(item => item.OwnerUserId == job.UserId)
                : query.Where(item => item.ViewerUserId == job.UserId);
            var rows = await query
                .AsNoTracking()
                .OrderBy(item => item.Id)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var row in rows.Where(item =>
                         item.CurrentState != RelationshipStates.Revoked))
            {
                await StageRevocationAsync(
                    row.OwnerUserId,
                    row.ViewerUserId,
                    row.RelationshipKind,
                    cancellationToken);
            }
            return Advance(job, rows, item => item.Id);
        }

        private async Task<StagePage> ProcessAnalyticsAsync(
            FollowingUserErasure job,
            CancellationToken cancellationToken)
        {
            var rows = await context.UserRelationsAnalytics
                .IgnoreQueryFilters()
                .Where(item => item.Id == job.UserId && item.ShardId > job.Cursor)
                .OrderBy(item => item.ShardId)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);

            // Cursor zero represents the first persisted analytics shard and must be
            // included on the first page.
            if (job.Cursor == 0)
            {
                var zero = await context.UserRelationsAnalytics
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(
                        item => item.Id == job.UserId && item.ShardId == 0,
                        cancellationToken);
                if (zero is not null && rows.All(item => item.ShardId != 0))
                    rows.Insert(0, zero);
                if (rows.Count > _options.BatchSize)
                    rows.RemoveAt(rows.Count - 1);
            }

            context.UserRelationsAnalytics.RemoveRange(rows);
            if (rows.Count > 0)
            {
                return new StagePage(
                    job.Stage,
                    rows.Max(item => (long)item.ShardId),
                    rows.Count);
            }

            return new StagePage(FollowingUserErasureStage.Complete, 0, 0);
        }

        private async Task DecrementAnalyticsAsync(
            int userId,
            int shardDiscriminator,
            AnalyticsCounter counter,
            CancellationToken cancellationToken)
        {
            var shardId = UserRelationAnalytics.ResolveShard(shardDiscriminator);
            var analytics = await context.UserRelationsAnalytics.SingleOrDefaultAsync(
                item => item.Id == userId && item.ShardId == shardId,
                cancellationToken);
            if (analytics is null)
                return;

            switch (counter)
            {
                case AnalyticsCounter.Friends:
                    analytics.DecrementFriends();
                    break;
                case AnalyticsCounter.Followers:
                    analytics.DecrementFollowers();
                    break;
                case AnalyticsCounter.Following:
                    analytics.DecrementFollowing();
                    break;
                case AnalyticsCounter.FriendRequests:
                    analytics.DecrementFriendRequests();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(counter));
            }
        }

        private async Task StageRevocationAsync(
            int ownerUserId,
            int viewerUserId,
            string kind,
            CancellationToken cancellationToken) =>
            await relationshipEvents.StageAsync(
                new RelationshipTransition(
                    ownerUserId,
                    viewerUserId,
                    kind,
                    RelationshipStates.Revoked,
                    "privacy.user.erasure"),
                cancellationToken);

        private async Task<List<T>> PageAsync<T>(
            IQueryable<T> query,
            CancellationToken cancellationToken)
            where T : FollowingEntity =>
            await query.OrderBy(item => item.Id)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);

        private static StagePage Advance<T>(
            FollowingUserErasure job,
            IReadOnlyList<T> rows)
            where T : FollowingEntity =>
            Advance(job, rows, item => (long)item.Id);

        private static StagePage Advance<T>(
            FollowingUserErasure job,
            IReadOnlyList<T> rows,
            Func<T, long> cursor)
        {
            if (rows.Count > 0)
                return new StagePage(job.Stage, cursor(rows[^1]), rows.Count);

            return new StagePage(
                (FollowingUserErasureStage)((byte)job.Stage + 1),
                0,
                0);
        }

        private async Task ReleaseAsync(
            int userId,
            long erasureVersion,
            string workerId,
            long leaseToken,
            CancellationToken cancellationToken)
        {
            context.ChangeTracker.Clear();
            var job = await context.UserErasures
                .SingleAsync(item => item.UserId == userId, cancellationToken);
            var nowUtc = NormalizeUtc(clock.UtcNow);
            if (job.ErasureVersion != erasureVersion
                || !job.OwnsLease(workerId, leaseToken, nowUtc))
                return;
            job.Release(workerId, leaseToken, nowUtc);
            await context.SaveChangesAsync(cancellationToken);
        }

        private static void ValidateWorker(string workerId)
        {
            if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128)
                throw new ArgumentException("A bounded worker ID is required.", nameof(workerId));
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private enum AnalyticsCounter
        {
            Friends,
            Followers,
            Following,
            FriendRequests,
        }

        private sealed record StagePage(
            FollowingUserErasureStage NextStage,
            long NextCursor,
            int Rows);

        private sealed record BatchResult(int Rows, bool Completed, bool LeaseLost)
        {
            public static BatchResult Lost { get; } = new(0, false, true);
        }
    }
}
