using Feed.Application.Features.Feeds;
using Feed.Application.Features.Privacy;
using Feed.Domain.AggregatesModel;
using MessageBrokerContracts.Feed;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class FeedFanoutProcessor(
        FeedContext context,
        IFeedUserErasureAuthority erasureAuthority,
        IOptions<FeedOptions> options,
        IOptions<AudienceProjectionOptions> audienceOptions,
        FeedTelemetry telemetry) : IFeedFanoutProcessor
    {
        private readonly FeedOptions _options = options.Value;
        private readonly AudienceProjectionOptions _audienceOptions = audienceOptions.Value;

        public async Task<int> ProcessBatchAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var observed = NormalizeUtc(observedAtUtc);
            var leaseDuration = TimeSpan.FromSeconds(_options.LeaseSeconds);
            var claimed = await ClaimAsync(
                workerId,
                observed,
                leaseDuration,
                cancellationToken);
            var processed = 0;
            foreach (var claim in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (claim.AttemptCount > _options.MaximumAttempts)
                {
                    var leaseToken = claim.LeaseToken
                        ?? throw new InvalidOperationException(
                            "Claimed Feed fanout work is missing its lease token.");
                    await using var terminal = await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted,
                        cancellationToken);
                    claim.Fail(
                        leaseToken,
                        "attempt_limit",
                        terminal: true,
                        observed);
                    await context.SaveChangesAsync(cancellationToken);
                    await terminal.CommitAsync(cancellationToken);
                    telemetry.Record("fanout", "attempt_limit", claim.Activity.DeliveryMode);
                    continue;
                }

                processed += await ProcessOneAsync(claim, observed, cancellationToken);
            }
            telemetry.RecordBatch("fanout", "push", processed);
            return processed;
        }

        private async Task<List<FanoutWorkItem>> ClaimAsync(
            string workerId,
            DateTime observedAtUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var claimed = await context.FanoutWorkItems
                .FromSqlInterpolated(
                    $"""
                    SELECT work.*
                    FROM `FanoutWorkItem` AS work
                    WHERE (
                        work.`State` = {FeedWorkStates.Pending}
                        OR (work.`State` = {FeedWorkStates.Processing}
                            AND work.`LeaseExpiresAtUtc` <= {observedAtUtc}))
                      AND NOT EXISTS (
                          SELECT 1
                          FROM `FanoutWorkItem` AS earlier
                          WHERE earlier.`PublisherUserId` = work.`PublisherUserId`
                            AND earlier.`Id` < work.`Id`
                            AND earlier.`State` NOT IN ({FeedWorkStates.Completed}, {FeedWorkStates.Failed}))
                    ORDER BY work.`UpdatedAtUtc`, work.`Id`
                    LIMIT {_options.FanoutClaimBatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .Include(work => work.Activity)
                .ToListAsync(cancellationToken);
            foreach (var work in claimed)
                work.Lease(workerId, observedAtUtc, leaseDuration);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }

        private async Task<int> ProcessOneAsync(
            FanoutWorkItem work,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (!work.LeaseToken.HasValue)
                return 0;
            var leaseToken = work.LeaseToken.Value;
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await using var publisherMutation = await erasureAuthority.AcquireMutationAsync(
                [work.PublisherUserId],
                cancellationToken);
            if (!publisherMutation.Allowed)
            {
                context.FanoutWorkItems.Remove(work);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                telemetry.Record("fanout", "erased_discarded", work.Activity.DeliveryMode);
                return 0;
            }
            var moderationSuppressed = await context.FeedModerationDecisionStates
                .AsNoTracking()
                .AnyAsync(decision => decision.IsSuppressed
                    && decision.ResourceKind == work.Activity.TargetType
                    && decision.ResourceId == work.Activity.TargetId,
                    cancellationToken);
            if (moderationSuppressed)
            {
                context.FanoutWorkItems.Remove(work);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                telemetry.Record("fanout", "moderation_suppressed", work.Activity.DeliveryMode);
                return 0;
            }
            var bootstrap = await context.FeedAudienceBootstrapStates
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    state => state.Id == FeedAudienceBootstrapState.SingletonId,
                    cancellationToken);
            if (bootstrap is null
                || !bootstrap.IsCompleteAndFresh(
                    observedAtUtc,
                    TimeSpan.FromSeconds(_audienceOptions.MaximumBootstrapAgeSeconds)))
            {
                work.DeferForProjection(
                    leaseToken,
                    "audience_projection_unavailable",
                    observedAtUtc);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                telemetry.Record("fanout", "projection_deferred", work.Activity.DeliveryMode);
                return 0;
            }

            var friendsOfFriendsIntermediates = await LoadFriendsOfFriendsIntermediatesAsync(
                work.Activity,
                observedAtUtc,
                cancellationToken);
            if (!work.AudienceHighWatermarkUserId.HasValue)
            {
                var highWatermark = await EligibleAudienceIds(
                        work.Activity,
                        observedAtUtc,
                        friendsOfFriendsIntermediates)
                    .Select(userId => (int?)userId)
                    .OrderByDescending(userId => userId)
                    .FirstOrDefaultAsync(cancellationToken) ?? 0;
                work.InitializeAudienceSnapshot(
                    leaseToken,
                    highWatermark,
                    observedAtUtc);
            }

            var cursor = work.NextViewerUserId ?? 0;
            var high = work.AudienceHighWatermarkUserId ?? 0;
            var batchSize = work.Activity.AudiencePolicy == FeedAudiencePolicies.FriendsOfFriends
                ? Math.Min(
                    _options.FanoutBatchSize,
                    _audienceOptions.MaximumFriendsOfFriendsFanoutBatchSize)
                : _options.FanoutBatchSize;
            var viewerIds = await EligibleAudienceIds(
                    work.Activity,
                    observedAtUtc,
                    friendsOfFriendsIntermediates)
                .Where(userId => userId > cursor && userId <= high)
                .Distinct()
                .OrderBy(id => id)
                .Take(batchSize + 1)
                .ToListAsync(cancellationToken);
            var hasMore = viewerIds.Count > batchSize;
            var page = viewerIds.Take(batchSize).ToArray();
            await using var recipientMutation = page.Length == 0
                ? null
                : await erasureAuthority.AcquireMutationAsync(page, cancellationToken);
            if (recipientMutation is not null && !recipientMutation.Allowed)
            {
                page = page.Where(userId =>
                        !recipientMutation.ErasedUserIds.Contains(userId))
                    .ToArray();
            }
            var addedRecipientIds = await AddEntriesAsync(
                work.Activity,
                page,
                observedAtUtc,
                cancellationToken);
            await BumpRevisionsAsync(addedRecipientIds, observedAtUtc, cancellationToken);

            var nextCursor = page.Length == 0 ? high : page[^1];
            work.Advance(
                leaseToken,
                nextCursor,
                completed: !hasMore,
                observedAtUtc);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            telemetry.Record(
                "fanout",
                hasMore ? "advanced" : "completed",
                work.Activity.DeliveryMode);
            return addedRecipientIds.Count;
        }

        private async Task<int[]> LoadFriendsOfFriendsIntermediatesAsync(
            FeedActivity activity,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (activity.AudiencePolicy != FeedAudiencePolicies.FriendsOfFriends)
                return [];
            var cutoff = observedAtUtc.AddSeconds(-_options.MaximumCheckpointAgeSeconds);
            var rows = await HealthyAudienceEdges(cutoff)
                .Where(edge => edge.OwnerUserId == activity.ActorUserId
                    && edge.RelationshipKind == RelationshipKinds.Friend
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == activity.ActorUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == activity.ActorUserId))))
                .Select(edge => edge.ViewerUserId)
                .Distinct()
                .OrderBy(userId => userId)
                .Take(_audienceOptions.MaximumFriendsOfFriendsCandidates + 1)
                .ToListAsync(cancellationToken);
            if (rows.Count > _audienceOptions.MaximumFriendsOfFriendsCandidates)
            {
                telemetry.Record("fanout", "fof_candidate_budget", activity.DeliveryMode);
                return [];
            }
            return rows.ToArray();
        }

        private IQueryable<int> EligibleAudienceIds(
            FeedActivity activity,
            DateTime observedAtUtc,
            IReadOnlyCollection<int> friendsOfFriendsIntermediates)
        {
            var cutoff = observedAtUtc.AddSeconds(-_options.MaximumCheckpointAgeSeconds);
            var requiredKind = activity.AudiencePolicy switch
            {
                FeedAudiencePolicies.Followers => RelationshipKinds.Follow,
                FeedAudiencePolicies.Friends => RelationshipKinds.Friend,
                FeedAudiencePolicies.FriendsOfFriends => RelationshipKinds.Friend,
                _ => null,
            };
            var direct = HealthyAudienceEdges(cutoff)
                .Where(edge => edge.OwnerUserId == activity.ActorUserId
                    && (requiredKind == null
                        ? edge.RelationshipKind == RelationshipKinds.Follow
                            || edge.RelationshipKind == RelationshipKinds.Friend
                        : edge.RelationshipKind == requiredKind)
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == activity.ActorUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == activity.ActorUserId))))
                .Select(edge => edge.ViewerUserId);
            if (activity.AudiencePolicy != FeedAudiencePolicies.FriendsOfFriends
                || friendsOfFriendsIntermediates.Count == 0)
            {
                return direct;
            }

            var intermediates = friendsOfFriendsIntermediates.ToArray();
            var twoHop = HealthyAudienceEdges(cutoff)
                .Where(edge => intermediates.Contains(edge.OwnerUserId)
                    && edge.RelationshipKind == RelationshipKinds.Friend
                    && edge.ViewerUserId != activity.ActorUserId
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == edge.OwnerUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == edge.OwnerUserId)
                            || (block.OwnerUserId == activity.ActorUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == activity.ActorUserId))))
                .Select(edge => edge.ViewerUserId);
            return direct.Union(twoHop);
        }

        private IQueryable<FeedAudienceEdge> HealthyAudienceEdges(DateTime cutoff) =>
            context.FeedAudienceEdges
                .Where(edge => edge.State == RelationshipStates.Active
                    && !context.FeedUserErasures.Any(erasure =>
                        erasure.UserId == edge.OwnerUserId
                        || erasure.UserId == edge.ViewerUserId))
                .Join(
                    context.FeedRelationshipCheckpoints.Where(checkpoint =>
                        checkpoint.Status == AudienceProjectionStates.Healthy
                        && checkpoint.LastConfirmedAtUtc >= cutoff),
                    edge => edge.SourceStreamId,
                    checkpoint => (Guid?)checkpoint.StreamId,
                    (edge, checkpoint) => new { edge, checkpoint })
                .Where(pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion)
                .Select(pair => pair.edge);

        private async Task<List<int>> AddEntriesAsync(
            FeedActivity activity,
            IReadOnlyCollection<int> viewerIds,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (viewerIds.Count == 0)
                return [];
            var ids = viewerIds.ToArray();
            var existing = await context.FeedEntries
                .Where(entry => entry.ActivityId == activity.Id
                    && ids.Contains(entry.RecipientUserId))
                .ToListAsync(cancellationToken);
            var existingIds = existing.Select(entry => entry.RecipientUserId).ToHashSet();
            var restored = existing.Where(entry => entry.RevokedAtUtc.HasValue).ToArray();
            foreach (var entry in restored)
                entry.Restore(observedAtUtc);
            var additions = ids.Where(id => !existingIds.Contains(id)).ToArray();
            foreach (var viewerId in additions)
            {
                await context.FeedEntries.AddAsync(
                    FeedEntry.Create(
                        activity,
                        viewerId,
                        FeedDeliveryModes.Push,
                        activity.RankKey,
                        observedAtUtc),
                    cancellationToken);
            }
            return additions.Concat(restored.Select(entry => entry.RecipientUserId)).ToList();
        }

        private async Task BumpRevisionsAsync(
            IReadOnlyCollection<int> userIds,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (userIds.Count == 0)
                return;
            var ids = userIds.Distinct().ToArray();
            var states = await context.FeedUserStates
                .Where(state => ids.Contains(state.UserId))
                .ToDictionaryAsync(state => state.UserId, cancellationToken);
            foreach (var userId in ids)
            {
                if (!states.TryGetValue(userId, out var state))
                {
                    state = FeedUserState.Create(userId, observedAtUtc);
                    await context.FeedUserStates.AddAsync(state, cancellationToken);
                    states.Add(userId, state);
                }
                state.IncrementRevision(observedAtUtc);
            }
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public sealed class FeedRevocationProcessor(
        FeedContext context,
        IFeedUserErasureAuthority erasureAuthority,
        IOptions<FeedOptions> options,
        IOptions<AudienceProjectionOptions> audienceOptions,
        FeedTelemetry telemetry,
        TimeProvider timeProvider) : IFeedRevocationProcessor
    {
        private readonly FeedOptions _options = options.Value;
        private readonly AudienceProjectionOptions _audienceOptions = audienceOptions.Value;

        public async Task<int> ProcessBatchAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var observed = NormalizeUtc(observedAtUtc);
            var claimed = await ClaimAsync(workerId, observed, cancellationToken);
            var revoked = 0;
            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work.AttemptCount > _options.MaximumAttempts)
                {
                    var leaseToken = work.LeaseToken
                        ?? throw new InvalidOperationException(
                            "Claimed Feed revocation work is missing its lease token.");
                    await using var terminal = await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted,
                        cancellationToken);
                    work.Fail(
                        leaseToken,
                        "attempt_limit",
                        terminal: true,
                        observed);
                    await context.SaveChangesAsync(cancellationToken);
                    await terminal.CommitAsync(cancellationToken);
                    telemetry.Record("revocation", "attempt_limit", "priority");
                    continue;
                }
                revoked += await ProcessOneAsync(work, observed, cancellationToken);
            }
            telemetry.RecordBatch("revocation", "priority", revoked);
            return revoked;
        }

        private async Task<List<FeedRevocationWorkItem>> ClaimAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var claimed = await context.FeedRevocationWorkItems
                .FromSqlInterpolated(
                    $"""
                    SELECT work.*
                    FROM `FeedRevocationWorkItem` AS work
                    WHERE work.`State` = {FeedWorkStates.Pending}
                       OR (work.`State` = {FeedWorkStates.Processing}
                           AND work.`LeaseExpiresAtUtc` <= {observedAtUtc})
                    ORDER BY work.`Priority` DESC, work.`UpdatedAtUtc`, work.`Id`
                    LIMIT {_options.FanoutClaimBatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .Include(work => work.Activity)
                .ToListAsync(cancellationToken);
            foreach (var work in claimed)
            {
                work.Lease(
                    workerId,
                    observedAtUtc,
                    TimeSpan.FromSeconds(_options.LeaseSeconds));
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }

        private async Task<int> ProcessOneAsync(
            FeedRevocationWorkItem work,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (!work.LeaseToken.HasValue)
                return 0;
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var principals = new[]
                {
                    work.OwnerUserId ?? 0,
                    work.ViewerUserId ?? 0,
                    work.Activity?.ActorUserId ?? 0,
                }
                .Where(userId => userId > 0)
                .Distinct()
                .ToArray();
            await using var principalMutation = principals.Length == 0
                ? null
                : await erasureAuthority.AcquireMutationAsync(principals, cancellationToken);
            var query = context.FeedEntries
                .Where(entry => entry.Id > work.NextEntryId && entry.RevokedAtUtc == null);
            if (work.RevocationKind == FeedRevocationKinds.Activity)
            {
                query = query.Where(entry => entry.ActivityId == work.ActivityId);
            }
            else if (work.RevocationKind == FeedRevocationKinds.FriendsOfFriends)
            {
                query = query.Where(entry =>
                    entry.Activity.AudiencePolicy == FeedAudiencePolicies.FriendsOfFriends
                    && (entry.Activity.ActorUserId == work.OwnerUserId
                        || entry.RecipientUserId == work.ViewerUserId));
            }
            else
            {
                query = query.Where(entry => entry.RecipientUserId == work.ViewerUserId
                    && entry.Activity.ActorUserId == work.OwnerUserId);
                if (work.RevocationKind == FeedRevocationKinds.Relationship)
                    query = query.Where(entry => entry.Activity.AudiencePolicy == work.AudiencePolicy);
            }

            var rows = await query
                .OrderBy(entry => entry.Id)
                .Select(entry => new RevocationCandidate(
                    entry.Id,
                    entry.RecipientUserId,
                    entry.Activity.ActorUserId))
                .Take(_options.RevocationBatchSize + 1)
                .ToListAsync(cancellationToken);
            var hasMore = rows.Count > _options.RevocationBatchSize;
            var page = rows.Take(_options.RevocationBatchSize).ToArray();
            var pageSubjects = page
                .SelectMany(item => new[] { item.ActorUserId, item.RecipientUserId })
                .Distinct()
                .ToArray();
            await using var pageMutation = pageSubjects.Length == 0
                ? null
                : await erasureAuthority.AcquireMutationAsync(pageSubjects, cancellationToken);
            var revokeIds = work.RevocationKind == FeedRevocationKinds.FriendsOfFriends
                ? await FindInvalidFriendsOfFriendsEntriesAsync(
                    page,
                    observedAtUtc,
                    cancellationToken)
                : page.Select(item => item.Id).ToArray();
            if (revokeIds.Length > 0)
            {
                await context.FeedEntries
                    .Where(entry => revokeIds.Contains(entry.Id) && entry.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(entry => entry.RevokedAtUtc, observedAtUtc)
                            .SetProperty(entry => entry.UpdatedAtUtc, observedAtUtc)
                            .SetProperty(entry => entry.Version, entry => entry.Version + 1),
                        cancellationToken);
                await BumpRevisionsAsync(
                    page.Where(item => revokeIds.Contains(item.Id))
                        .Select(item => item.RecipientUserId)
                        .Where(userId => pageMutation is null
                            || !pageMutation.ErasedUserIds.Contains(userId))
                        .Distinct()
                        .ToArray(),
                    observedAtUtc,
                    cancellationToken);
            }

            work.Advance(
                work.LeaseToken.Value,
                page.Length == 0 ? work.NextEntryId : page[^1].Id,
                completed: !hasMore,
                observedAtUtc);
            if (!hasMore)
            {
                var actorErased = work.Activity is not null
                    && principalMutation is not null
                    && principalMutation.ErasedUserIds.Contains(work.Activity.ActorUserId);
                if (!actorErased)
                    await SchedulePostRevocationFanoutAsync(work, observedAtUtc, cancellationToken);
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            telemetry.Record("revocation", hasMore ? "advanced" : "completed");
            if (!hasMore)
            {
                telemetry.RecordRevocationCompletion(
                    work.RevocationKind,
                    work.SourceOccurredAtUtc,
                    timeProvider.GetUtcNow().UtcDateTime);
            }
            return revokeIds.Length;
        }

        private async Task<long[]> FindInvalidFriendsOfFriendsEntriesAsync(
            IReadOnlyCollection<RevocationCandidate> page,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (page.Count == 0)
                return [];
            var nonOwner = page.Where(item => item.ActorUserId != item.RecipientUserId).ToArray();
            if (nonOwner.Length == 0)
                return [];

            var bootstrap = await context.FeedAudienceBootstrapStates
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    state => state.Id == FeedAudienceBootstrapState.SingletonId,
                    cancellationToken);
            if (bootstrap is null
                || !bootstrap.IsCompleteAndFresh(
                    observedAtUtc,
                    TimeSpan.FromSeconds(_audienceOptions.MaximumBootstrapAgeSeconds)))
            {
                return nonOwner.Select(item => item.Id).ToArray();
            }

            var actors = nonOwner.Select(item => item.ActorUserId).Distinct().ToArray();
            var recipients = nonOwner.Select(item => item.RecipientUserId).Distinct().ToArray();
            var pairBudget = _options.RevocationBatchSize;
            var blockedRows = await context.FeedAudienceEdges
                .AsNoTracking()
                .Where(edge => edge.RelationshipKind == RelationshipKinds.Block
                    && edge.State == RelationshipStates.Blocked
                    && ((actors.Contains(edge.OwnerUserId)
                            && recipients.Contains(edge.ViewerUserId))
                        || (recipients.Contains(edge.OwnerUserId)
                            && actors.Contains(edge.ViewerUserId))))
                .Select(edge => new { edge.OwnerUserId, edge.ViewerUserId })
                .Take(pairBudget + 1)
                .ToListAsync(cancellationToken);
            if (blockedRows.Count > pairBudget)
                return nonOwner.Select(item => item.Id).ToArray();
            var blockedPairs = blockedRows
                .SelectMany(item => new[]
                {
                    new RelationshipPair(item.OwnerUserId, item.ViewerUserId),
                    new RelationshipPair(item.ViewerUserId, item.OwnerUserId),
                })
                .ToHashSet();

            var cutoff = observedAtUtc.AddSeconds(-_options.MaximumCheckpointAgeSeconds);
            var directRows = await HealthyFriendEdges(cutoff)
                .Where(edge => actors.Contains(edge.OwnerUserId)
                    && recipients.Contains(edge.ViewerUserId))
                .Select(edge => new { edge.OwnerUserId, edge.ViewerUserId })
                .Take(pairBudget + 1)
                .ToListAsync(cancellationToken);
            if (directRows.Count > pairBudget)
                return nonOwner.Select(item => item.Id).ToArray();
            var directPairs = directRows
                .Select(item => new RelationshipPair(item.OwnerUserId, item.ViewerUserId))
                .ToHashSet();

            var firstHopRows = await HealthyFriendEdges(cutoff)
                .Where(edge => actors.Contains(edge.OwnerUserId)
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == edge.OwnerUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == edge.OwnerUserId))))
                .Select(edge => new
                {
                    ActorUserId = edge.OwnerUserId,
                    IntermediateUserId = edge.ViewerUserId,
                })
                .Distinct()
                .Take(_audienceOptions.MaximumFriendsOfFriendsCandidates + 1)
                .ToListAsync(cancellationToken);
            if (firstHopRows.Count > _audienceOptions.MaximumFriendsOfFriendsCandidates)
                return nonOwner.Select(item => item.Id).ToArray();

            var intermediates = firstHopRows
                .Select(item => item.IntermediateUserId)
                .Distinct()
                .ToArray();
            var secondHopRows = await HealthyFriendEdges(cutoff)
                .Where(edge => intermediates.Contains(edge.OwnerUserId)
                    && recipients.Contains(edge.ViewerUserId)
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == edge.OwnerUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == edge.OwnerUserId))))
                .Select(edge => new
                {
                    IntermediateUserId = edge.OwnerUserId,
                    RecipientUserId = edge.ViewerUserId,
                })
                .Distinct()
                .Take(_audienceOptions.MaximumFriendsOfFriendsCandidates + 1)
                .ToListAsync(cancellationToken);
            if (secondHopRows.Count > _audienceOptions.MaximumFriendsOfFriendsCandidates)
                return nonOwner.Select(item => item.Id).ToArray();

            var secondHops = secondHopRows
                .Select(item => new RelationshipPair(
                    item.IntermediateUserId,
                    item.RecipientUserId))
                .ToHashSet();
            var pathPairs = firstHopRows
                .SelectMany(first => recipients
                    .Where(recipient => secondHops.Contains(new RelationshipPair(
                        first.IntermediateUserId,
                        recipient)))
                    .Select(recipient => new RelationshipPair(
                        first.ActorUserId,
                        recipient)))
                .ToHashSet();

            return nonOwner
                .Where(item =>
                {
                    var pair = new RelationshipPair(item.ActorUserId, item.RecipientUserId);
                    return blockedPairs.Contains(pair)
                        || (!directPairs.Contains(pair) && !pathPairs.Contains(pair));
                })
                .Select(item => item.Id)
                .ToArray();
        }

        private IQueryable<FeedAudienceEdge> HealthyFriendEdges(DateTime cutoff) =>
            context.FeedAudienceEdges
                .AsNoTracking()
                .Where(edge => edge.RelationshipKind == RelationshipKinds.Friend
                    && edge.State == RelationshipStates.Active
                    && !context.FeedUserErasures.Any(erasure =>
                        erasure.UserId == edge.OwnerUserId
                        || erasure.UserId == edge.ViewerUserId))
                .Join(
                    context.FeedRelationshipCheckpoints.AsNoTracking().Where(checkpoint =>
                        checkpoint.Status == AudienceProjectionStates.Healthy
                        && checkpoint.LastConfirmedAtUtc >= cutoff),
                    edge => edge.SourceStreamId,
                    checkpoint => (Guid?)checkpoint.StreamId,
                    (edge, checkpoint) => new { edge, checkpoint })
                .Where(pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion)
                .Select(pair => pair.edge);

        private async Task SchedulePostRevocationFanoutAsync(
            FeedRevocationWorkItem work,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (work.RevocationKind != FeedRevocationKinds.Activity
                || work.Activity is null
                || work.Activity.IsTombstoned
                || work.Activity.DeliveryMode != FeedDeliveryModes.Push
                || work.Activity.AudiencePolicy == FeedAudiencePolicies.Owner)
            {
                return;
            }
            var source = $"revocation-replay:{work.Id}:{work.Activity.SourceVersion}";
            if (context.FanoutWorkItems.Local.Any(item => item.SourceEventId == source)
                || await context.FanoutWorkItems.AnyAsync(
                    item => item.SourceEventId == source,
                    cancellationToken))
            {
                return;
            }
            await context.FanoutWorkItems.AddAsync(
                FanoutWorkItem.Create(
                    source,
                    work.Activity,
                    observedAtUtc,
                    FeedWorkKinds.Replay),
                cancellationToken);
        }

        private async Task BumpRevisionsAsync(
            IReadOnlyCollection<int> userIds,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (userIds.Count == 0)
                return;
            var ids = userIds.ToArray();
            var states = await context.FeedUserStates
                .Where(state => ids.Contains(state.UserId))
                .ToDictionaryAsync(state => state.UserId, cancellationToken);
            foreach (var userId in ids)
            {
                if (!states.TryGetValue(userId, out var state))
                {
                    state = FeedUserState.Create(userId, observedAtUtc);
                    await context.FeedUserStates.AddAsync(state, cancellationToken);
                }
                state.IncrementRevision(observedAtUtc);
            }
        }

        private sealed record RevocationCandidate(
            long Id,
            int RecipientUserId,
            int ActorUserId);

        private readonly record struct RelationshipPair(int OwnerUserId, int ViewerUserId);

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
