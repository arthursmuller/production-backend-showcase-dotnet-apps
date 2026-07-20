using Feed.Application.Features.Audience;
using Feed.Application.Features.Feeds;
using Feed.Application.Features.Privacy;
using Feed.Domain.AggregatesModel;
using MessageBrokerContracts.Feed;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class AudienceProjectionOptions
    {
        public const string SectionName = "AudienceProjection";

        public int MaximumBootstrapPageSize { get; set; } = 500;
        public int MaximumQuarantineDrain { get; set; } = 100;
        public int MaximumCheckpointAgeSeconds { get; set; } = 86400;
        public int MaximumBootstrapAgeSeconds { get; set; } = 3600;
        public int BootstrapIntervalSeconds { get; set; } = 300;
        public int BootstrapRetrySeconds { get; set; } = 15;
        public int BootstrapPageDelayMilliseconds { get; set; } = 50;
        public int BootstrapLeaseSeconds { get; set; } = 60;
        public int AuthorizationDeadlineMilliseconds { get; set; } = 500;
        public int MaximumFriendsOfFriendsCandidates { get; set; } = 256;
        public int MaximumFriendsOfFriendsFanoutBatchSize { get; set; } = 100;
    }

    public sealed class AudienceProjectionStore(
        FeedContext context,
        IFeedUserErasureAuthority erasureAuthority,
        AudienceProjectionTelemetry telemetry,
        Microsoft.Extensions.Options.IOptions<AudienceProjectionOptions> options,
        Microsoft.Extensions.Options.IOptions<FeedOptions> feedOptions,
        TimeProvider timeProvider)
        : IRelationshipProjectionWriter, IRelationshipProjectionReconciler, IAudienceProjectionReader
    {
        private const int ReconciliationQueryBatchSize = 100;
        private readonly AudienceProjectionOptions _options = options.Value;
        private readonly FeedOptions _feedOptions = feedOptions.Value;

        public async Task<RelationshipProjectionResult> ApplyAsync(
            RelationshipStateChanged message,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var incoming = Validate(message);
            var observed = NormalizeUtc(observedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();
            await using var mutation = await erasureAuthority.AcquireMutationAsync(
                [incoming.OwnerUserId, incoming.ViewerUserId],
                cancellationToken);
            if (!mutation.Allowed)
            {
                await mutation.CommitAsync(cancellationToken);
                telemetry.Record(incoming.RelationshipKind, "erased_ignored");
                return new RelationshipProjectionResult(
                    RelationshipProjectionOutcome.StaleOrDuplicate,
                    incoming.StreamId,
                    0,
                    1);
            }

            var checkpoint = await context.FeedRelationshipCheckpoints
                .SingleOrDefaultAsync(item => item.StreamId == incoming.StreamId, cancellationToken);
            if (checkpoint is null)
            {
                var conflictingCheckpoint = await context.FeedRelationshipCheckpoints
                    .SingleOrDefaultAsync(
                        item => item.OwnerUserId == incoming.OwnerUserId
                            && item.ViewerUserId == incoming.ViewerUserId
                            && item.RelationshipKind == incoming.RelationshipKind,
                        cancellationToken);
                if (conflictingCheckpoint is not null)
                {
                    await QuarantineAsync(
                        conflictingCheckpoint,
                        incoming,
                        "stream_identity_mismatch",
                        observed,
                        cancellationToken);
                    await ApplyRelationshipEffectsAsync(
                        [RelationshipEffect.FailClosed(
                            incoming.EventId,
                            conflictingCheckpoint.OwnerUserId,
                            conflictingCheckpoint.ViewerUserId,
                            conflictingCheckpoint.RelationshipKind,
                            incoming.OccurredAtUtc)],
                        observed,
                        cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await mutation.CommitAsync(cancellationToken);
                    telemetry.Record(incoming.RelationshipKind, "invalid_quarantined");
                    return Result(
                        RelationshipProjectionOutcome.InvalidQuarantined,
                        incoming.StreamId,
                        conflictingCheckpoint);
                }

                checkpoint = FeedRelationshipCheckpoint.Create(
                    incoming.StreamId,
                    incoming.OwnerUserId,
                    incoming.ViewerUserId,
                    incoming.RelationshipKind,
                    observed);
                await context.FeedRelationshipCheckpoints.AddAsync(checkpoint, cancellationToken);
            }

            var disposition = checkpoint.Classify(
                incoming.StreamId,
                incoming.OwnerUserId,
                incoming.ViewerUserId,
                incoming.RelationshipKind,
                incoming.StreamVersion);
            if (disposition == ProjectionVersionDisposition.StaleOrDuplicate)
            {
                if (incoming.StreamVersion == checkpoint.LastAppliedVersion)
                {
                    var appliedEdge = await context.FeedAudienceEdges.SingleOrDefaultAsync(
                        item => item.OwnerUserId == incoming.OwnerUserId
                            && item.ViewerUserId == incoming.ViewerUserId
                            && item.RelationshipKind == incoming.RelationshipKind,
                        cancellationToken);
                    var isExactDuplicate = checkpoint.LastEventId == incoming.EventId
                        && appliedEdge is not null
                        && appliedEdge.SourceStreamId == incoming.StreamId
                        && appliedEdge.LastEventId == incoming.EventId
                        && appliedEdge.SourceVersion == incoming.StreamVersion
                        && string.Equals(
                            appliedEdge.State,
                            incoming.State,
                            StringComparison.Ordinal);
                    if (!isExactDuplicate)
                    {
                        await QuarantineAsync(
                            checkpoint,
                            incoming,
                            "version_replay_conflict",
                            observed,
                            cancellationToken);
                        await ApplyRelationshipEffectsAsync(
                            [RelationshipEffect.FailClosed(
                                incoming.EventId,
                                checkpoint.OwnerUserId,
                                checkpoint.ViewerUserId,
                                checkpoint.RelationshipKind,
                                incoming.OccurredAtUtc)],
                            observed,
                            cancellationToken);
                        await context.SaveChangesAsync(cancellationToken);
                        await mutation.CommitAsync(cancellationToken);
                        telemetry.Record(incoming.RelationshipKind, "invalid_quarantined");
                        return Result(
                            RelationshipProjectionOutcome.InvalidQuarantined,
                            incoming.StreamId,
                            checkpoint);
                    }
                }
                telemetry.Record(incoming.RelationshipKind, "stale_or_duplicate");
                return Result(
                    RelationshipProjectionOutcome.StaleOrDuplicate,
                    incoming.StreamId,
                    checkpoint);
            }
            if (disposition == ProjectionVersionDisposition.StreamIdentityMismatch)
            {
                await QuarantineAsync(
                    checkpoint,
                    incoming,
                    "stream_metadata_mismatch",
                    observed,
                    cancellationToken);
                await ApplyRelationshipEffectsAsync(
                    [RelationshipEffect.FailClosed(
                        incoming.EventId,
                        checkpoint.OwnerUserId,
                        checkpoint.ViewerUserId,
                        checkpoint.RelationshipKind,
                        incoming.OccurredAtUtc)],
                    observed,
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                telemetry.Record(incoming.RelationshipKind, "invalid_quarantined");
                return Result(
                    RelationshipProjectionOutcome.InvalidQuarantined,
                    incoming.StreamId,
                    checkpoint);
            }
            if (disposition == ProjectionVersionDisposition.Gap)
            {
                checkpoint.MarkGap(incoming.StreamVersion, observed);
                await MarkBootstrapDegradedAsync(
                    "relationship_version_gap",
                    observed,
                    cancellationToken);
                await AddQuarantineIfMissingAsync(
                    incoming,
                    checkpoint.LastAppliedVersion + 1,
                    "version_gap",
                    observed,
                    cancellationToken);
                await ApplyRelationshipEffectsAsync(
                    [RelationshipEffect.FailClosed(
                        incoming.EventId,
                        checkpoint.OwnerUserId,
                        checkpoint.ViewerUserId,
                        checkpoint.RelationshipKind,
                        incoming.OccurredAtUtc)],
                    observed,
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                telemetry.Record(incoming.RelationshipKind, "gap_quarantined");
                return Result(
                    RelationshipProjectionOutcome.GapQuarantined,
                    incoming.StreamId,
                    checkpoint);
            }

            var edge = await context.FeedAudienceEdges.SingleOrDefaultAsync(
                item => item.OwnerUserId == incoming.OwnerUserId
                    && item.ViewerUserId == incoming.ViewerUserId
                    && item.RelationshipKind == incoming.RelationshipKind,
                cancellationToken);
            var hasOtherAudienceMembership = await HasOtherAudienceMembershipAsync(
                incoming.OwnerUserId,
                incoming.ViewerUserId,
                incoming.RelationshipKind,
                cancellationToken);
            var wasAudienceEligible = hasOtherAudienceMembership || IsAudienceEligible(
                incoming.RelationshipKind,
                edge?.State,
                checkpoint.Status);
            if (edge is null)
            {
                edge = FeedAudienceEdge.Create(
                    incoming.OwnerUserId,
                    incoming.ViewerUserId,
                    incoming.RelationshipKind,
                    incoming.State,
                    incoming.StreamId,
                    incoming.EventId,
                    incoming.StreamVersion,
                    incoming.OccurredAtUtc,
                    observed);
                await context.FeedAudienceEdges.AddAsync(edge, cancellationToken);
            }
            else if (edge.SourceStreamId != incoming.StreamId
                     || edge.SourceVersion != checkpoint.LastAppliedVersion)
            {
                await QuarantineAsync(
                    checkpoint,
                    incoming,
                    edge.SourceStreamId.HasValue
                        ? "edge_stream_mismatch"
                        : "edge_stream_missing",
                    observed,
                    cancellationToken);
                await ApplyRelationshipEffectsAsync(
                    [RelationshipEffect.FailClosed(
                        incoming.EventId,
                        checkpoint.OwnerUserId,
                        checkpoint.ViewerUserId,
                        checkpoint.RelationshipKind,
                        incoming.OccurredAtUtc)],
                    observed,
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                telemetry.Record(incoming.RelationshipKind, "invalid_quarantined");
                return Result(
                    RelationshipProjectionOutcome.InvalidQuarantined,
                    incoming.StreamId,
                    checkpoint);
            }
            else
            {
                edge.ApplyNext(
                    incoming.StreamId,
                    incoming.EventId,
                    incoming.State,
                    incoming.StreamVersion,
                    incoming.OccurredAtUtc,
                    observed);
            }

            checkpoint.MarkApplied(
                incoming.EventId,
                incoming.StreamVersion,
                incoming.OccurredAtUtc,
                observed);
            var replayedOccurrences = await DrainContiguousQuarantineAsync(
                checkpoint,
                edge,
                observed,
                cancellationToken);
            await ApplyRelationshipEffectsAsync(
                [RelationshipEffect.Applied(
                    incoming.EventId,
                    incoming.OwnerUserId,
                    incoming.ViewerUserId,
                    incoming.RelationshipKind,
                    incoming.State,
                    incoming.OccurredAtUtc)],
                observed,
                cancellationToken);
            await ApplyPublisherAudienceDeltaAsync(
                incoming.OwnerUserId,
                incoming.RelationshipKind,
                wasAudienceEligible,
                hasOtherAudienceMembership
                    || IsAudienceEligible(incoming.RelationshipKind, edge.State, checkpoint.Status),
                observed,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            telemetry.Record(incoming.RelationshipKind, "applied");
            var committed = timeProvider.GetUtcNow().UtcDateTime;
            telemetry.RecordConvergence("event", incoming.OccurredAtUtc, committed);
            foreach (var sourceOccurredAtUtc in replayedOccurrences)
                telemetry.RecordConvergence("event", sourceOccurredAtUtc, committed);
            return Result(
                RelationshipProjectionOutcome.Applied,
                incoming.StreamId,
                checkpoint);
        }

        public async Task<int> ReconcilePageAsync(
            IReadOnlyCollection<RelationshipStateSnapshot> snapshots,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshots);
            if (snapshots.Count > _options.MaximumBootstrapPageSize)
                throw new ArgumentOutOfRangeException(nameof(snapshots));
            if (snapshots.Count == 0)
                return 0;

            var observed = NormalizeUtc(observedAtUtc);
            var validated = snapshots.Select(Validate).ToArray();
            if (validated.Select(item => item.StreamId).Distinct().Count() != validated.Length)
                throw new ArgumentException("A reconciliation page cannot repeat a stream.", nameof(snapshots));

            var subjects = validated
                .SelectMany(item => new[] { item.OwnerUserId, item.ViewerUserId })
                .Distinct()
                .ToArray();
            await using var mutation = await erasureAuthority.AcquireMutationAsync(
                subjects,
                cancellationToken);
            if (!mutation.Allowed)
            {
                validated = validated.Where(item =>
                        !mutation.ErasedUserIds.Contains(item.OwnerUserId)
                        && !mutation.ErasedUserIds.Contains(item.ViewerUserId))
                    .ToArray();
            }
            if (validated.Length == 0)
            {
                await mutation.CommitAsync(cancellationToken);
                return 0;
            }

            var streamIds = validated.Select(item => item.StreamId).ToArray();
            var checkpointsByStream = await context.FeedRelationshipCheckpoints
                .Where(item => streamIds.Contains(item.StreamId))
                .ToListAsync(cancellationToken);
            var checkpointsByDirection = await LoadCheckpointsByExactDirectionAsync(
                validated,
                cancellationToken);
            var checkpoints = checkpointsByStream
                .Concat(checkpointsByDirection)
                .GroupBy(item => item.StreamId)
                .Select(group => group.First())
                .ToList();
            var edges = await LoadEdgesByExactDirectionAsync(validated, cancellationToken);
            var quarantines = await context.FeedRelationshipEventQuarantines
                .Where(item => streamIds.Contains(item.StreamId) && item.ResolvedAtUtc == null)
                .ToListAsync(cancellationToken);

            var byStream = checkpoints.ToDictionary(item => item.StreamId);
            var checkpointsByKey = checkpoints.ToDictionary(Key);
            var edgesByKey = edges.ToDictionary(Key);
            var reconciled = 0;
            var reconciledSourceOccurrences = new List<DateTime>(validated.Length);
            var relationshipEffects = new List<RelationshipEffect>(validated.Length);
            var publishersToRefresh = new HashSet<int>();

            foreach (var snapshot in validated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = Key(snapshot.OwnerUserId, snapshot.ViewerUserId, snapshot.RelationshipKind);
                if (!byStream.TryGetValue(snapshot.StreamId, out var checkpoint))
                {
                    if (checkpointsByKey.TryGetValue(key, out var conflicting))
                    {
                        conflicting.MarkQuarantined(snapshot.StreamVersion, observed);
                        telemetry.Record(snapshot.RelationshipKind, "snapshot_stream_mismatch");
                        continue;
                    }

                    checkpoint = FeedRelationshipCheckpoint.Create(
                        snapshot.StreamId,
                        snapshot.OwnerUserId,
                        snapshot.ViewerUserId,
                        snapshot.RelationshipKind,
                        observed);
                    await context.FeedRelationshipCheckpoints.AddAsync(checkpoint, cancellationToken);
                    byStream.Add(snapshot.StreamId, checkpoint);
                    checkpointsByKey.Add(key, checkpoint);
                }

                if (checkpoint.Classify(
                        snapshot.StreamId,
                        snapshot.OwnerUserId,
                        snapshot.ViewerUserId,
                        snapshot.RelationshipKind,
                        snapshot.StreamVersion)
                    == ProjectionVersionDisposition.StreamIdentityMismatch)
                {
                    checkpoint.MarkQuarantined(snapshot.StreamVersion, observed);
                    telemetry.Record(snapshot.RelationshipKind, "snapshot_metadata_mismatch");
                    continue;
                }
                if (snapshot.StreamVersion < checkpoint.LastAppliedVersion)
                    continue;

                if (!edgesByKey.TryGetValue(key, out var edge))
                {
                    edge = FeedAudienceEdge.Create(
                        snapshot.OwnerUserId,
                        snapshot.ViewerUserId,
                        snapshot.RelationshipKind,
                        snapshot.State,
                        snapshot.StreamId,
                        snapshot.LastEventId,
                        snapshot.StreamVersion,
                        snapshot.AsOfUtc,
                        observed);
                    await context.FeedAudienceEdges.AddAsync(edge, cancellationToken);
                    edgesByKey.Add(key, edge);
                }
                else if (edge.SourceStreamId.HasValue && edge.SourceStreamId != snapshot.StreamId)
                {
                    checkpoint.MarkQuarantined(snapshot.StreamVersion, observed);
                    telemetry.Record(snapshot.RelationshipKind, "snapshot_edge_mismatch");
                    continue;
                }
                else if (snapshot.StreamVersion == checkpoint.LastAppliedVersion
                    && (checkpoint.LastEventId != snapshot.LastEventId
                        || edge.SourceStreamId != snapshot.StreamId
                        || edge.LastEventId != snapshot.LastEventId
                        || edge.SourceVersion != snapshot.StreamVersion
                        || !string.Equals(edge.State, snapshot.State, StringComparison.Ordinal)))
                {
                    checkpoint.MarkQuarantined(snapshot.StreamVersion, observed);
                    telemetry.Record(snapshot.RelationshipKind, "snapshot_version_conflict");
                    continue;
                }
                else
                {
                    edge.Reconcile(
                        snapshot.StreamId,
                        snapshot.LastEventId,
                        snapshot.State,
                        snapshot.StreamVersion,
                        snapshot.AsOfUtc,
                        observed);
                }

                checkpoint.Reconcile(
                    snapshot.LastEventId,
                    snapshot.StreamVersion,
                    snapshot.AsOfUtc,
                    observed);
                foreach (var quarantine in quarantines.Where(item =>
                             item.StreamId == snapshot.StreamId
                             && item.ReceivedVersion <= snapshot.StreamVersion))
                {
                    quarantine.Resolve("authority_snapshot", observed);
                }
                relationshipEffects.Add(RelationshipEffect.Applied(
                    snapshot.LastEventId,
                    snapshot.OwnerUserId,
                    snapshot.ViewerUserId,
                    snapshot.RelationshipKind,
                    snapshot.State,
                    snapshot.AsOfUtc));
                if (snapshot.RelationshipKind is RelationshipKinds.Follow
                    or RelationshipKinds.Friend)
                    publishersToRefresh.Add(snapshot.OwnerUserId);
                reconciled++;
                reconciledSourceOccurrences.Add(snapshot.AsOfUtc);
                telemetry.Record(snapshot.RelationshipKind, "reconciled");
            }

            await ApplyRelationshipEffectsAsync(
                relationshipEffects,
                observed,
                cancellationToken);
            var hasUnhealthyProjection = await context.FeedRelationshipCheckpoints
                .AnyAsync(item => item.Status != AudienceProjectionStates.Healthy, cancellationToken);
            var hasUnresolvedProjectionQuarantine = await context.FeedRelationshipEventQuarantines
                .AnyAsync(item => item.ResolvedAtUtc == null, cancellationToken);
            if (hasUnhealthyProjection || hasUnresolvedProjectionQuarantine)
            {
                await MarkBootstrapDegradedAsync(
                    "relationship_snapshot_incomplete",
                    observed,
                    cancellationToken);
            }
            await using var ownedTransaction = context.Database.CurrentTransaction is null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await context.SaveChangesAsync(cancellationToken);
            await RefreshPublisherAudienceCountsAsync(
                publishersToRefresh,
                observed,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            var committed = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var sourceOccurredAtUtc in reconciledSourceOccurrences)
                telemetry.RecordConvergence("snapshot", sourceOccurredAtUtc, committed);
            return reconciled;
        }

        private async Task<List<FeedRelationshipCheckpoint>> LoadCheckpointsByExactDirectionAsync(
            IReadOnlyCollection<ValidRelationshipSnapshot> snapshots,
            CancellationToken cancellationToken)
        {
            var results = new Dictionary<Guid, FeedRelationshipCheckpoint>();
            foreach (var batch in snapshots.Chunk(ReconciliationQueryBatchSize))
            {
                var predicate = BuildCheckpointDirectionPredicate(batch);
                var rows = await context.FeedRelationshipCheckpoints
                    .Where(predicate)
                    .ToListAsync(cancellationToken);
                foreach (var row in rows)
                    results[row.StreamId] = row;
            }
            return results.Values.ToList();
        }

        private async Task<List<FeedAudienceEdge>> LoadEdgesByExactDirectionAsync(
            IReadOnlyCollection<ValidRelationshipSnapshot> snapshots,
            CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, FeedAudienceEdge>(StringComparer.Ordinal);
            foreach (var batch in snapshots.Chunk(ReconciliationQueryBatchSize))
            {
                var predicate = BuildEdgeDirectionPredicate(batch);
                var rows = await context.FeedAudienceEdges
                    .Where(predicate)
                    .ToListAsync(cancellationToken);
                foreach (var row in rows)
                    results[Key(row)] = row;
            }
            return results.Values.ToList();
        }

        private static Expression<Func<FeedRelationshipCheckpoint, bool>>
            BuildCheckpointDirectionPredicate(IEnumerable<ValidRelationshipSnapshot> snapshots)
        {
            var item = Expression.Parameter(typeof(FeedRelationshipCheckpoint), "item");
            Expression body = Expression.Constant(false);
            foreach (var snapshot in snapshots)
            {
                var direction = Expression.AndAlso(
                    Expression.Equal(
                        Expression.Property(item, nameof(FeedRelationshipCheckpoint.OwnerUserId)),
                        Expression.Constant(snapshot.OwnerUserId)),
                    Expression.AndAlso(
                        Expression.Equal(
                            Expression.Property(item, nameof(FeedRelationshipCheckpoint.ViewerUserId)),
                            Expression.Constant(snapshot.ViewerUserId)),
                        Expression.Equal(
                            Expression.Property(item, nameof(FeedRelationshipCheckpoint.RelationshipKind)),
                            Expression.Constant(snapshot.RelationshipKind))));
                body = Expression.OrElse(body, direction);
            }
            return Expression.Lambda<Func<FeedRelationshipCheckpoint, bool>>(body, item);
        }

        private static Expression<Func<FeedAudienceEdge, bool>> BuildEdgeDirectionPredicate(
            IEnumerable<ValidRelationshipSnapshot> snapshots)
        {
            var item = Expression.Parameter(typeof(FeedAudienceEdge), "item");
            Expression body = Expression.Constant(false);
            foreach (var snapshot in snapshots)
            {
                var direction = Expression.AndAlso(
                    Expression.Equal(
                        Expression.Property(item, nameof(FeedAudienceEdge.OwnerUserId)),
                        Expression.Constant(snapshot.OwnerUserId)),
                    Expression.AndAlso(
                        Expression.Equal(
                            Expression.Property(item, nameof(FeedAudienceEdge.ViewerUserId)),
                            Expression.Constant(snapshot.ViewerUserId)),
                        Expression.Equal(
                            Expression.Property(item, nameof(FeedAudienceEdge.RelationshipKind)),
                            Expression.Constant(snapshot.RelationshipKind))));
                body = Expression.OrElse(body, direction);
            }
            return Expression.Lambda<Func<FeedAudienceEdge, bool>>(body, item);
        }

        public async Task<AudienceAccessDecision> EvaluateAsync(
            int ownerUserId,
            int viewerUserId,
            string audiencePolicy,
            DateTime evaluatedAtUtc,
            TimeSpan maximumCheckpointAge,
            CancellationToken cancellationToken)
        {
            if (ownerUserId <= 0 || viewerUserId <= 0)
                return AudienceAccessDecision.Deny("invalid_identity");
            var erased = await erasureAuthority.GetErasedAsync(
                [ownerUserId, viewerUserId],
                cancellationToken);
            if (erased.Count > 0)
                return AudienceAccessDecision.Deny("erased_identity");
            if (ownerUserId == viewerUserId)
                return AudienceAccessDecision.Allow("owner");
            if (maximumCheckpointAge <= TimeSpan.Zero)
                return AudienceAccessDecision.Deny("invalid_freshness_window");

            var suppliedPolicy = audiencePolicy?.Trim().ToLowerInvariant();
            var policy = FeedAudiencePolicies.NormalizeOrOwner(suppliedPolicy);
            if (policy == AudiencePolicies.Owner)
                return AudienceAccessDecision.Deny(
                    suppliedPolicy == AudiencePolicies.Owner ? "owner_only" : "unsupported_policy");

            var evaluated = NormalizeUtc(evaluatedAtUtc);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(
                _options.AuthorizationDeadlineMilliseconds));
            try
            {
                var decision = await EvaluateNonOwnerAsync(
                    ownerUserId,
                    viewerUserId,
                    policy,
                    evaluated,
                    maximumCheckpointAge,
                    deadline.Token);
                telemetry.RecordAuthorization(policy, decision.Allowed ? "allowed" : "denied");
                return decision;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                && deadline.IsCancellationRequested)
            {
                telemetry.RecordAuthorization(policy, "budget_denied");
                return AudienceAccessDecision.Deny("authorization_budget_exceeded");
            }
        }

        private async Task<AudienceAccessDecision> EvaluateNonOwnerAsync(
            int ownerUserId,
            int viewerUserId,
            string policy,
            DateTime evaluatedAtUtc,
            TimeSpan maximumCheckpointAge,
            CancellationToken cancellationToken)
        {
            var bootstrap = await context.FeedAudienceBootstrapStates
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    state => state.Id == FeedAudienceBootstrapState.SingletonId,
                    cancellationToken);
            if (bootstrap is null)
                return AudienceAccessDecision.Deny("bootstrap_missing");
            if (!bootstrap.IsCompleteAndFresh(
                evaluatedAtUtc,
                TimeSpan.FromSeconds(_options.MaximumBootstrapAgeSeconds)))
            {
                return AudienceAccessDecision.Deny(
                    bootstrap.Status == AudienceBootstrapStates.Healthy
                        ? "bootstrap_stale"
                        : "bootstrap_incomplete");
            }

            var checkpoints = await context.FeedRelationshipCheckpoints
                .AsNoTracking()
                .Where(item => (item.OwnerUserId == ownerUserId
                        && item.ViewerUserId == viewerUserId)
                    || (item.RelationshipKind == RelationshipKinds.Block
                        && item.OwnerUserId == viewerUserId
                        && item.ViewerUserId == ownerUserId))
                .Take(9)
                .ToListAsync(cancellationToken);
            var edges = await context.FeedAudienceEdges
                .AsNoTracking()
                .Where(item => (item.OwnerUserId == ownerUserId
                        && item.ViewerUserId == viewerUserId)
                    || (item.RelationshipKind == RelationshipKinds.Block
                        && item.OwnerUserId == viewerUserId
                        && item.ViewerUserId == ownerUserId))
                .Take(9)
                .ToListAsync(cancellationToken);
            if (checkpoints.Count >= 9 || edges.Count >= 9)
                return AudienceAccessDecision.Deny("authorization_projection_overflow");

            var blockDecision = EvaluateBlockDirections(
                checkpoints,
                edges,
                evaluatedAtUtc,
                maximumCheckpointAge);
            if (!blockDecision.Allowed)
                return blockDecision;
            if (policy == AudiencePolicies.Public)
                return AudienceAccessDecision.Allow("public");

            var directionalCheckpoints = checkpoints.Where(item =>
                item.OwnerUserId == ownerUserId
                && item.ViewerUserId == viewerUserId).ToArray();
            var directionalEdges = edges.Where(item =>
                item.OwnerUserId == ownerUserId
                && item.ViewerUserId == viewerUserId).ToArray();
            var requiredKind = policy == AudiencePolicies.Followers
                ? RelationshipKinds.Follow
                : RelationshipKinds.Friend;
            var requiredDecision = EvaluateKind(
                requiredKind,
                directionalCheckpoints,
                directionalEdges,
                evaluatedAtUtc,
                maximumCheckpointAge,
                allowMissing: policy == AudiencePolicies.FriendsOfFriends);
            if (requiredDecision.Allowed)
            {
                var requiredEdge = directionalEdges.SingleOrDefault(item =>
                    item.RelationshipKind == requiredKind);
                if (requiredEdge?.State == RelationshipStates.Active)
                    return AudienceAccessDecision.Allow($"active_{requiredKind}");
            }
            if (policy != AudiencePolicies.FriendsOfFriends)
            {
                return requiredDecision.Allowed
                    ? AudienceAccessDecision.Deny($"inactive_{requiredKind}")
                    : requiredDecision;
            }

            var cutoff = evaluatedAtUtc - maximumCheckpointAge;
            var candidates = await HealthyFriendEdges(cutoff)
                .Where(edge => edge.OwnerUserId == ownerUserId
                    && edge.ViewerUserId != viewerUserId
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == ownerUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == ownerUserId))))
                .Select(edge => edge.ViewerUserId)
                .Distinct()
                .OrderBy(userId => userId)
                .Take(_options.MaximumFriendsOfFriendsCandidates + 1)
                .ToListAsync(cancellationToken);
            if (candidates.Count > _options.MaximumFriendsOfFriendsCandidates)
                return AudienceAccessDecision.Deny("friends_of_friends_candidate_budget");
            if (candidates.Count == 0)
                return AudienceAccessDecision.Deny("no_friends_of_friends_path");

            var intermediateIds = candidates.ToArray();
            var hasTwoHopPath = await HealthyFriendEdges(cutoff)
                .Where(edge => intermediateIds.Contains(edge.OwnerUserId)
                    && edge.ViewerUserId == viewerUserId
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == edge.OwnerUserId
                                && block.ViewerUserId == viewerUserId)
                            || (block.OwnerUserId == viewerUserId
                                && block.ViewerUserId == edge.OwnerUserId))))
                .AnyAsync(cancellationToken);
            return hasTwoHopPath
                ? AudienceAccessDecision.Allow("active_friends_of_friends")
                : AudienceAccessDecision.Deny("no_friends_of_friends_path");
        }

        private IQueryable<FeedAudienceEdge> HealthyFriendEdges(DateTime cutoff) =>
            context.FeedAudienceEdges
                .AsNoTracking()
                .Where(edge => edge.RelationshipKind == RelationshipKinds.Friend
                    && edge.State == RelationshipStates.Active)
                .Join(
                    context.FeedRelationshipCheckpoints.AsNoTracking().Where(checkpoint =>
                        checkpoint.Status == AudienceProjectionStates.Healthy
                        && checkpoint.LastConfirmedAtUtc >= cutoff),
                    edge => edge.SourceStreamId,
                    checkpoint => (Guid?)checkpoint.StreamId,
                    (edge, checkpoint) => new { edge, checkpoint })
                .Where(pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion)
                .Select(pair => pair.edge);

        private async Task<IReadOnlyList<DateTime>> DrainContiguousQuarantineAsync(
            FeedRelationshipCheckpoint checkpoint,
            FeedAudienceEdge edge,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var replayedOccurrences = new List<DateTime>();
            var pending = await context.FeedRelationshipEventQuarantines
                .Where(item => item.StreamId == checkpoint.StreamId && item.ResolvedAtUtc == null)
                .OrderBy(item => item.ReceivedVersion)
                .Take(_options.MaximumQuarantineDrain + 1)
                .ToListAsync(cancellationToken);
            var processed = 0;
            foreach (var item in pending)
            {
                if (item.ReceivedVersion <= checkpoint.LastAppliedVersion)
                {
                    item.Resolve("stale_after_apply", observedAtUtc);
                    continue;
                }
                if (item.ReceivedVersion != checkpoint.LastAppliedVersion + 1)
                {
                    checkpoint.MarkGap(item.ReceivedVersion, observedAtUtc);
                    await MarkBootstrapDegradedAsync(
                        "quarantine_version_gap",
                        observedAtUtc,
                        cancellationToken);
                    break;
                }
                if (processed >= _options.MaximumQuarantineDrain)
                {
                    checkpoint.MarkQuarantined(item.ReceivedVersion, observedAtUtc);
                    await MarkBootstrapDegradedAsync(
                        "quarantine_drain_budget",
                        observedAtUtc,
                        cancellationToken);
                    break;
                }

                edge.ApplyNext(
                    item.StreamId,
                    item.EventId,
                    item.State,
                    item.ReceivedVersion,
                    item.SourceOccurredAtUtc,
                    observedAtUtc);
                checkpoint.MarkApplied(
                    item.EventId,
                    item.ReceivedVersion,
                    item.SourceOccurredAtUtc,
                    observedAtUtc);
                await ApplyRelationshipEffectsAsync(
                    [RelationshipEffect.Applied(
                        item.EventId,
                        item.OwnerUserId,
                        item.ViewerUserId,
                        item.RelationshipKind,
                        item.State,
                        item.SourceOccurredAtUtc)],
                    observedAtUtc,
                    cancellationToken);
                item.Resolve("contiguous_replay", observedAtUtc);
                replayedOccurrences.Add(item.SourceOccurredAtUtc);
                processed++;
                telemetry.Record(item.RelationshipKind, "quarantine_replayed");
            }
            return replayedOccurrences;
        }

        private async Task QuarantineAsync(
            FeedRelationshipCheckpoint checkpoint,
            ValidRelationshipEvent incoming,
            string reason,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            checkpoint.MarkQuarantined(incoming.StreamVersion, observedAtUtc);
            await MarkBootstrapDegradedAsync(reason, observedAtUtc, cancellationToken);
            await AddQuarantineIfMissingAsync(
                incoming,
                checkpoint.LastAppliedVersion + 1,
                reason,
                observedAtUtc,
                cancellationToken);
        }

        private async Task MarkBootstrapDegradedAsync(
            string reason,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var state = context.FeedAudienceBootstrapStates.Local.SingleOrDefault(item =>
                    item.Id == FeedAudienceBootstrapState.SingletonId)
                ?? await context.FeedAudienceBootstrapStates.SingleOrDefaultAsync(
                    item => item.Id == FeedAudienceBootstrapState.SingletonId,
                    cancellationToken);
            if (state is null)
            {
                state = FeedAudienceBootstrapState.Create(observedAtUtc);
                await context.FeedAudienceBootstrapStates.AddAsync(state, cancellationToken);
            }
            state.MarkDegraded(reason, observedAtUtc);
            telemetry.RecordBootstrap("projection_degraded");
        }

        private async Task AddQuarantineIfMissingAsync(
            ValidRelationshipEvent incoming,
            long expectedVersion,
            string reason,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            var exists = await context.FeedRelationshipEventQuarantines.AnyAsync(
                item => item.EventId == incoming.EventId
                    || (item.StreamId == incoming.StreamId
                        && item.ReceivedVersion == incoming.StreamVersion),
                cancellationToken);
            if (exists)
                return;

            await context.FeedRelationshipEventQuarantines.AddAsync(
                FeedRelationshipEventQuarantine.Create(
                    incoming.EventId,
                    incoming.StreamId,
                    incoming.OwnerUserId,
                    incoming.ViewerUserId,
                    incoming.RelationshipKind,
                    incoming.State,
                    incoming.StreamVersion,
                    expectedVersion,
                    reason,
                    incoming.OccurredAtUtc,
                    observedAtUtc),
                cancellationToken);
        }

        private async Task ApplyRelationshipEffectsAsync(
            IReadOnlyCollection<RelationshipEffect> effects,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (effects.Count == 0)
                return;

            // One revision increment per affected user is sufficient to invalidate all
            // first-page cache variants for this transaction, regardless of how many
            // relationship snapshots were reconciled in the bounded page.
            var affectedUsers = effects
                .SelectMany(effect => effect.RelationshipKind == RelationshipKinds.Block
                    ? new[] { effect.OwnerUserId, effect.ViewerUserId }
                    : new[] { effect.ViewerUserId })
                .Distinct()
                .ToArray();
            var states = await context.FeedUserStates
                .Where(state => affectedUsers.Contains(state.UserId))
                .ToDictionaryAsync(state => state.UserId, cancellationToken);
            foreach (var userId in affectedUsers)
            {
                if (!states.TryGetValue(userId, out var state))
                {
                    state = context.FeedUserStates.Local.SingleOrDefault(item => item.UserId == userId)
                        ?? FeedUserState.Create(userId, observedAtUtc);
                    if (context.Entry(state).State == EntityState.Detached)
                        await context.FeedUserStates.AddAsync(state, cancellationToken);
                    states[userId] = state;
                }
                state.IncrementRevision(observedAtUtc);
            }

            var revocations = effects
                .SelectMany(BuildRevocationRequests)
                .GroupBy(request => request.SourceEventId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            if (revocations.Length == 0)
                return;

            var sources = revocations.Select(request => request.SourceEventId).ToArray();
            var persisted = await context.FeedRevocationWorkItems
                .Where(work => sources.Contains(work.SourceEventId))
                .Select(work => work.SourceEventId)
                .ToListAsync(cancellationToken);
            var existing = persisted
                .Concat(context.FeedRevocationWorkItems.Local.Select(work => work.SourceEventId))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var request in revocations)
            {
                if (!existing.Add(request.SourceEventId))
                    continue;
                var work = request.IsFriendsOfFriends
                    ? FeedRevocationWorkItem.ForFriendsOfFriendsRelationship(
                        request.SourceEventId,
                        request.OwnerUserId,
                        request.ViewerUserId,
                        request.SourceOccurredAtUtc,
                        observedAtUtc)
                    : FeedRevocationWorkItem.ForRelationship(
                        request.SourceEventId,
                        request.OwnerUserId,
                        request.ViewerUserId,
                        request.AudiencePolicy,
                        request.IsBlock,
                        request.SourceOccurredAtUtc,
                        observedAtUtc);
                await context.FeedRevocationWorkItems.AddAsync(work, cancellationToken);
            }
        }

        private Task ApplyPublisherAudienceDeltaAsync(
            int publisherUserId,
            string relationshipKind,
            bool wasEligible,
            bool isEligible,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (relationshipKind == RelationshipKinds.Block
                || wasEligible == isEligible)
            {
                return Task.CompletedTask;
            }
            return ApplyPublisherAudienceDeltasAsync(
                new Dictionary<int, long>
                {
                    [publisherUserId] = isEligible ? 1 : -1,
                },
                observedAtUtc,
                cancellationToken);
        }

        private async Task ApplyPublisherAudienceDeltasAsync(
            IReadOnlyDictionary<int, long> deltas,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (deltas.Count == 0)
                return;
            var publisherIds = deltas.Keys.ToArray();
            var states = await context.FeedPublisherStates
                .Where(state => publisherIds.Contains(state.PublisherUserId))
                .ToListAsync(cancellationToken);
            foreach (var state in states)
            {
                var count = Math.Max(0, state.AudienceCount + deltas[state.PublisherUserId]);
                state.ObserveAudienceCount(
                    count,
                    _feedOptions.PullEnterAudienceCount,
                    _feedOptions.PullExitAudienceCount,
                    observedAtUtc);
            }
        }

        private async Task RefreshPublisherAudienceCountsAsync(
            IReadOnlyCollection<int> publisherIds,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (publisherIds.Count == 0)
                return;
            var ids = publisherIds.ToArray();
            var states = await context.FeedPublisherStates
                .Where(state => ids.Contains(state.PublisherUserId))
                .ToListAsync(cancellationToken);
            if (states.Count == 0)
                return;
            var stateIds = states.Select(state => state.PublisherUserId).ToArray();
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
                .Select(pair => new
                {
                    pair.edge.OwnerUserId,
                    pair.edge.ViewerUserId,
                })
                .Distinct()
                .GroupBy(item => item.OwnerUserId)
                .Select(group => new
                {
                    PublisherUserId = group.Key,
                    AudienceCount = group.LongCount(),
                })
                .ToDictionaryAsync(
                    item => item.PublisherUserId,
                    item => item.AudienceCount,
                    cancellationToken);
            foreach (var state in states)
            {
                state.ObserveAudienceCount(
                    counts.GetValueOrDefault(state.PublisherUserId),
                    _feedOptions.PullEnterAudienceCount,
                    _feedOptions.PullExitAudienceCount,
                    observedAtUtc);
            }
        }

        private Task<bool> HasOtherAudienceMembershipAsync(
            int ownerUserId,
            int viewerUserId,
            string incomingKind,
            CancellationToken cancellationToken)
        {
            if (incomingKind == RelationshipKinds.Block)
                return Task.FromResult(false);
            return context.FeedAudienceEdges
                .Where(edge => edge.OwnerUserId == ownerUserId
                    && edge.ViewerUserId == viewerUserId
                    && edge.RelationshipKind != incomingKind
                    && (edge.RelationshipKind == RelationshipKinds.Follow
                        || edge.RelationshipKind == RelationshipKinds.Friend)
                    && edge.State == RelationshipStates.Active)
                .Join(
                    context.FeedRelationshipCheckpoints.Where(checkpoint =>
                        checkpoint.Status == AudienceProjectionStates.Healthy),
                    edge => edge.SourceStreamId,
                    checkpoint => (Guid?)checkpoint.StreamId,
                    (edge, checkpoint) => new { edge, checkpoint })
                .AnyAsync(
                    pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion,
                    cancellationToken);
        }

        private static bool IsAudienceEligible(
            string relationshipKind,
            string? state,
            string checkpointStatus) =>
            relationshipKind is RelationshipKinds.Follow or RelationshipKinds.Friend
            && state == RelationshipStates.Active
            && checkpointStatus == AudienceProjectionStates.Healthy;

        private static IEnumerable<RevocationRequest> BuildRevocationRequests(
            RelationshipEffect effect)
        {
            var isBlock = effect.RelationshipKind == RelationshipKinds.Block;
            var mustRevoke = effect.IsFailClosed
                || (isBlock
                    ? effect.State == RelationshipStates.Blocked
                    : effect.State == RelationshipStates.Revoked);
            if (!mustRevoke)
                yield break;

            var policy = effect.RelationshipKind == RelationshipKinds.Friend
                ? FeedAudiencePolicies.Friends
                : FeedAudiencePolicies.Followers;
            var prefix = isBlock ? "block" : "relationship";
            yield return new RevocationRequest(
                $"{prefix}:{effect.EventId:N}:{effect.OwnerUserId}:{effect.ViewerUserId}",
                effect.OwnerUserId,
                effect.ViewerUserId,
                policy,
                isBlock,
                false,
                effect.SourceOccurredAtUtc);
            if (isBlock || effect.RelationshipKind == RelationshipKinds.Friend)
            {
                yield return new RevocationRequest(
                    $"fof:{effect.EventId:N}:{effect.OwnerUserId}:{effect.ViewerUserId}",
                    effect.OwnerUserId,
                    effect.ViewerUserId,
                    FeedAudiencePolicies.FriendsOfFriends,
                    false,
                    true,
                    effect.SourceOccurredAtUtc);
            }
            if (isBlock)
            {
                yield return new RevocationRequest(
                    $"{prefix}:{effect.EventId:N}:{effect.ViewerUserId}:{effect.OwnerUserId}",
                    effect.ViewerUserId,
                    effect.OwnerUserId,
                    FeedAudiencePolicies.Public,
                    true,
                    false,
                    effect.SourceOccurredAtUtc);
                yield return new RevocationRequest(
                    $"fof:{effect.EventId:N}:{effect.ViewerUserId}:{effect.OwnerUserId}",
                    effect.ViewerUserId,
                    effect.OwnerUserId,
                    FeedAudiencePolicies.FriendsOfFriends,
                    false,
                    true,
                    effect.SourceOccurredAtUtc);
            }
        }

        private static AudienceAccessDecision EvaluateKind(
            string relationshipKind,
            IReadOnlyCollection<FeedRelationshipCheckpoint> checkpoints,
            IReadOnlyCollection<FeedAudienceEdge> edges,
            DateTime evaluatedAtUtc,
            TimeSpan maximumCheckpointAge,
            bool allowMissing)
        {
            var checkpoint = checkpoints.SingleOrDefault(item =>
                item.RelationshipKind == relationshipKind);
            if (checkpoint is null)
            {
                return allowMissing
                    ? AudienceAccessDecision.Allow($"no_{relationshipKind}_stream")
                    : AudienceAccessDecision.Deny($"missing_{relationshipKind}_checkpoint");
            }
            if (!checkpoint.IsFresh(evaluatedAtUtc, maximumCheckpointAge))
                return AudienceAccessDecision.Deny($"stale_or_unhealthy_{relationshipKind}_checkpoint");

            var edge = edges.SingleOrDefault(item => item.RelationshipKind == relationshipKind);
            if (edge is null
                || edge.SourceStreamId != checkpoint.StreamId
                || edge.SourceVersion != checkpoint.LastAppliedVersion)
            {
                return AudienceAccessDecision.Deny($"inconsistent_{relationshipKind}_projection");
            }
            return AudienceAccessDecision.Allow($"healthy_{relationshipKind}_projection");
        }

        private static AudienceAccessDecision EvaluateBlockDirections(
            IReadOnlyCollection<FeedRelationshipCheckpoint> checkpoints,
            IReadOnlyCollection<FeedAudienceEdge> edges,
            DateTime evaluatedAtUtc,
            TimeSpan maximumCheckpointAge)
        {
            var blockCheckpoints = checkpoints.Where(item =>
                item.RelationshipKind == RelationshipKinds.Block).ToArray();
            var blockEdges = edges.Where(item =>
                item.RelationshipKind == RelationshipKinds.Block).ToArray();
            if (blockCheckpoints.Length == 0 && blockEdges.Length == 0)
                return AudienceAccessDecision.Allow("no_block_stream");

            foreach (var checkpoint in blockCheckpoints)
            {
                if (!checkpoint.IsFresh(evaluatedAtUtc, maximumCheckpointAge))
                    return AudienceAccessDecision.Deny("stale_or_unhealthy_block_checkpoint");

                var edge = blockEdges.SingleOrDefault(item =>
                    item.OwnerUserId == checkpoint.OwnerUserId
                    && item.ViewerUserId == checkpoint.ViewerUserId);
                if (edge is null
                    || edge.SourceStreamId != checkpoint.StreamId
                    || edge.SourceVersion != checkpoint.LastAppliedVersion)
                {
                    return AudienceAccessDecision.Deny("inconsistent_block_projection");
                }
                if (edge.State == RelationshipStates.Blocked)
                    return AudienceAccessDecision.Deny("blocked");
            }

            var hasOrphanedEdge = blockEdges.Any(edge => !blockCheckpoints.Any(checkpoint =>
                checkpoint.OwnerUserId == edge.OwnerUserId
                && checkpoint.ViewerUserId == edge.ViewerUserId));
            return hasOrphanedEdge
                ? AudienceAccessDecision.Deny("inconsistent_block_projection")
                : AudienceAccessDecision.Allow("healthy_block_projection");
        }

        private static RelationshipProjectionResult Result(
            RelationshipProjectionOutcome outcome,
            Guid streamId,
            FeedRelationshipCheckpoint checkpoint) =>
            new(
                outcome,
                streamId,
                checkpoint.LastAppliedVersion,
                checkpoint.LastAppliedVersion + 1);

        private static ValidRelationshipEvent Validate(RelationshipStateChanged message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.EventId == Guid.Empty || message.StreamId == Guid.Empty)
                throw new ArgumentException("Event and stream IDs are required.", nameof(message));
            if (message.StreamVersion <= 0
                || message.OwnerUserId <= 0
                || message.ViewerUserId <= 0
                || message.OwnerUserId == message.ViewerUserId)
            {
                throw new ArgumentException("Invalid relationship identity or version.", nameof(message));
            }
            if (string.IsNullOrWhiteSpace(message.ReasonCode)
                || message.ReasonCode.Trim().Length > FeedSchemaLimits.ErrorCodeLength)
            {
                throw new ArgumentException("A bounded reason code is required.", nameof(message));
            }
            return new ValidRelationshipEvent(
                message.EventId,
                message.StreamId,
                message.StreamVersion,
                message.OwnerUserId,
                message.ViewerUserId,
                NormalizeKind(message.RelationshipKind),
                NormalizeState(message.RelationshipKind, message.State),
                NormalizeUtc(message.OccurredAtUtc));
        }

        private static ValidRelationshipSnapshot Validate(RelationshipStateSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.Cursor <= 0)
                throw new ArgumentException("Invalid relationship snapshot.", nameof(snapshot));
            if (snapshot.StreamId == Guid.Empty
                || snapshot.LastEventId == Guid.Empty
                || snapshot.StreamVersion <= 0
                || snapshot.OwnerUserId <= 0
                || snapshot.ViewerUserId <= 0
                || snapshot.OwnerUserId == snapshot.ViewerUserId)
            {
                throw new ArgumentException("Invalid snapshot identity or version.", nameof(snapshot));
            }
            return new ValidRelationshipSnapshot(
                snapshot.Cursor,
                snapshot.StreamId,
                snapshot.LastEventId,
                snapshot.StreamVersion,
                snapshot.OwnerUserId,
                snapshot.ViewerUserId,
                NormalizeKind(snapshot.RelationshipKind),
                NormalizeState(snapshot.RelationshipKind, snapshot.State),
                NormalizeUtc(snapshot.AsOfUtc));
        }

        private static string NormalizeKind(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized)
                || !RelationshipKinds.IsSupported(normalized))
                throw new ArgumentException("Unsupported relationship kind.", nameof(value));
            return normalized;
        }

        private static string NormalizeState(string kind, string value)
        {
            var normalizedKind = NormalizeKind(kind);
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized)
                || !RelationshipStates.IsSupported(normalized))
                throw new ArgumentException("Unsupported relationship state.", nameof(value));
            if (normalizedKind == RelationshipKinds.Block
                && normalized == RelationshipStates.Active)
            {
                throw new ArgumentException("Block state must be blocked or revoked.", nameof(value));
            }
            if (normalizedKind != RelationshipKinds.Block
                && normalized == RelationshipStates.Blocked)
            {
                throw new ArgumentException("Blocked state requires a block stream.", nameof(value));
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

        private static string Key(FeedRelationshipCheckpoint item) =>
            Key(item.OwnerUserId, item.ViewerUserId, item.RelationshipKind);

        private static string Key(FeedAudienceEdge item) =>
            Key(item.OwnerUserId, item.ViewerUserId, item.RelationshipKind);

        private static string Key(int ownerUserId, int viewerUserId, string kind) =>
            $"{ownerUserId}:{viewerUserId}:{kind}";

        private sealed record ValidRelationshipEvent(
            Guid EventId,
            Guid StreamId,
            long StreamVersion,
            int OwnerUserId,
            int ViewerUserId,
            string RelationshipKind,
            string State,
            DateTime OccurredAtUtc);

        private sealed record ValidRelationshipSnapshot(
            long Cursor,
            Guid StreamId,
            Guid LastEventId,
            long StreamVersion,
            int OwnerUserId,
            int ViewerUserId,
            string RelationshipKind,
            string State,
            DateTime AsOfUtc);

        private sealed record RelationshipEffect(
            Guid EventId,
            int OwnerUserId,
            int ViewerUserId,
            string RelationshipKind,
            string State,
            DateTime SourceOccurredAtUtc,
            bool IsFailClosed)
        {
            public static RelationshipEffect Applied(
                Guid eventId,
                int ownerUserId,
                int viewerUserId,
                string relationshipKind,
                string state,
                DateTime sourceOccurredAtUtc) =>
                new(
                    eventId,
                    ownerUserId,
                    viewerUserId,
                    relationshipKind,
                    state,
                    sourceOccurredAtUtc,
                    false);

            public static RelationshipEffect FailClosed(
                Guid eventId,
                int ownerUserId,
                int viewerUserId,
                string relationshipKind,
                DateTime sourceOccurredAtUtc) =>
                new(
                    eventId,
                    ownerUserId,
                    viewerUserId,
                    relationshipKind,
                    RelationshipStates.Revoked,
                    sourceOccurredAtUtc,
                    true);
        }

        private sealed record RevocationRequest(
            string SourceEventId,
            int OwnerUserId,
            int ViewerUserId,
            string AudiencePolicy,
            bool IsBlock,
            bool IsFriendsOfFriends,
            DateTime SourceOccurredAtUtc);
    }
}
