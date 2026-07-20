using Comment.Application.Features.Privacy;
using Comment.Application.Features.Visibility;
using Comment.Domain.AggregatesModel.Visibility;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Visibility
{
    public sealed class CommentRelationshipProjectionStore(
        CommentContext context,
        CommentAudienceProjectionTelemetry telemetry,
        ICommentUserErasureAuthority erasure)
        : ICommentRelationshipProjectionWriter, ICommentRelationshipProjectionReconciler
    {
        public async Task<CommentRelationshipProjectionResult> ApplyAsync(
            RelationshipStateChanged message,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            var observed = Utc(observedAtUtc);
            var participants = new[] { message.OwnerUserId, message.ViewerUserId }
                .Where(value => value > 0)
                .Distinct()
                .ToArray();
            var acquiredMutation = participants.Length == 0
                ? null
                : await erasure.AcquireMutationAsync(participants, cancellationToken);
            await using var mutation = acquiredMutation;
            if (mutation is not null && !mutation.Allowed)
            {
                await mutation.CommitAsync(cancellationToken);
                var erasedKind = message.RelationshipKind?.Trim().ToLowerInvariant();
                telemetry.Event(
                    erasedKind is not null && RelationshipKinds.IsSupported(erasedKind)
                        ? erasedKind
                        : "other",
                    "erased");
                return new(CommentRelationshipProjectionOutcome.ErasedSuppressed,
                    message.StreamId, 0, 0);
            }

            if (!TryValidate(message, out var kind, out var state))
            {
                var result = await QuarantineInvalidAsync(message, observed, cancellationToken);
                if (mutation is not null)
                    await mutation.CommitAsync(cancellationToken);
                return result;
            }

            await using var transaction = context.Database.CurrentTransaction is null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var checkpoint = await context.CommentRelationshipCheckpoints
                .SingleOrDefaultAsync(item => item.StreamId == message.StreamId, cancellationToken);
            if (checkpoint is null)
            {
                var conflicting = await context.CommentRelationshipCheckpoints.SingleOrDefaultAsync(item =>
                    item.OwnerUserId == message.OwnerUserId && item.ViewerUserId == message.ViewerUserId
                    && item.RelationshipKind == kind, cancellationToken);
                if (conflicting is not null)
                {
                    conflicting.Quarantine(message.StreamVersion, observed);
                    await FailCloseEdgeAsync(message, kind, observed, cancellationToken);
                    await AddQuarantineAsync(message, kind, state,
                        conflicting.LastAppliedVersion + 1, "stream_identity_mismatch", observed,
                        cancellationToken);
                    await DegradeGlobalAsync("stream_identity_mismatch", observed, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await CommitAsync(transaction, mutation, cancellationToken);
                    telemetry.Event(kind, "invalid");
                    return new(CommentRelationshipProjectionOutcome.InvalidQuarantined,
                        message.StreamId, conflicting.LastAppliedVersion,
                        conflicting.LastAppliedVersion + 1);
                }
                checkpoint = CommentRelationshipCheckpoint.Create(
                    message.StreamId, message.OwnerUserId, message.ViewerUserId, kind, observed);
                await context.CommentRelationshipCheckpoints.AddAsync(checkpoint, cancellationToken);
            }

            var disposition = checkpoint.Classify(message.StreamId, message.OwnerUserId,
                message.ViewerUserId, kind, message.StreamVersion);
            if (disposition == CommentProjectionVersionDisposition.StaleOrDuplicate)
            {
                if (checkpoint.LastAppliedVersion == message.StreamVersion
                    && checkpoint.LastEventId == message.EventId)
                {
                    var duplicateEdge = await context.CommentRelationshipEdges.AsNoTracking()
                        .SingleOrDefaultAsync(item => item.OwnerUserId == message.OwnerUserId
                            && item.ViewerUserId == message.ViewerUserId
                            && item.RelationshipKind == kind, cancellationToken);
                    if (duplicateEdge is not null && duplicateEdge.SourceStreamId == message.StreamId
                        && duplicateEdge.LastEventId == message.EventId
                        && duplicateEdge.SourceVersion == message.StreamVersion
                        && duplicateEdge.State == state)
                    {
                        telemetry.Event(kind, "duplicate");
                        await CommitAsync(transaction, mutation, cancellationToken);
                        return new(CommentRelationshipProjectionOutcome.StaleOrDuplicate, message.StreamId,
                            checkpoint.LastAppliedVersion, checkpoint.LastAppliedVersion + 1);
                    }
                }
                checkpoint.Quarantine(message.StreamVersion, observed);
                await AddQuarantineAsync(message, kind, state, checkpoint.LastAppliedVersion + 1,
                    "replay_conflict", observed, cancellationToken);
                await DegradeGlobalAsync("replay_conflict", observed, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, mutation, cancellationToken);
                telemetry.Event(kind, "invalid");
                return new(CommentRelationshipProjectionOutcome.InvalidQuarantined, message.StreamId,
                    checkpoint.LastAppliedVersion, checkpoint.LastAppliedVersion + 1);
            }

            if (disposition is CommentProjectionVersionDisposition.Gap
                or CommentProjectionVersionDisposition.IdentityMismatch)
            {
                if (disposition == CommentProjectionVersionDisposition.Gap)
                    checkpoint.Gap(message.StreamVersion, observed);
                else
                    checkpoint.Quarantine(message.StreamVersion, observed);
                await FailCloseEdgeAsync(message, kind, observed, cancellationToken);
                await AddQuarantineAsync(message, kind, state, checkpoint.LastAppliedVersion + 1,
                    disposition == CommentProjectionVersionDisposition.Gap ? "version_gap" : "identity_mismatch",
                    observed, cancellationToken);
                await DegradeGlobalAsync("relationship_gap", observed, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, mutation, cancellationToken);
                telemetry.Event(kind, disposition == CommentProjectionVersionDisposition.Gap ? "gap" : "invalid");
                return new(CommentRelationshipProjectionOutcome.GapQuarantined, message.StreamId,
                    checkpoint.LastAppliedVersion, checkpoint.LastAppliedVersion + 1);
            }

            checkpoint.Apply(message.EventId, message.StreamVersion, message.OccurredAtUtc, observed);
            await UpsertEdgeAsync(message.OwnerUserId, message.ViewerUserId, kind, state,
                message.StreamId, message.EventId, message.StreamVersion, message.OccurredAtUtc,
                observed, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, mutation, cancellationToken);
            telemetry.Event(kind, "applied");
            return new(CommentRelationshipProjectionOutcome.Applied, message.StreamId,
                message.StreamVersion, message.StreamVersion + 1);
        }

        public async Task<int> ReconcilePageAsync(
            IReadOnlyCollection<RelationshipStateSnapshot> snapshots,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshots);
            if (snapshots.Count > 500)
                throw new ArgumentOutOfRangeException(nameof(snapshots), "A reconciliation page cannot exceed 500 rows.");
            var observed = Utc(observedAtUtc);
            var activeSnapshots = snapshots;
            ICommentUserMutationScope? mutation = null;
            if (erasure is not null && snapshots.Count > 0)
            {
                var participants = snapshots.SelectMany(value =>
                    new[] { value.OwnerUserId, value.ViewerUserId }).Distinct().ToArray();
                var erasedUsers = await erasure.GetErasedAsync(participants, cancellationToken);
                activeSnapshots = snapshots.Where(value =>
                    !erasedUsers.Contains(value.OwnerUserId)
                    && !erasedUsers.Contains(value.ViewerUserId)).ToArray();
                var activeParticipants = activeSnapshots.SelectMany(value =>
                    new[] { value.OwnerUserId, value.ViewerUserId }).Distinct().ToArray();
                if (activeParticipants.Length > 0)
                {
                    mutation = await erasure.AcquireMutationAsync(activeParticipants, cancellationToken);
                    if (!mutation.Allowed)
                    {
                        await mutation.CommitAsync(cancellationToken);
                        await mutation.DisposeAsync();
                        throw new InvalidOperationException(
                            "An erasure raced the Comment relationship snapshot page; retry the fixed cursor.");
                    }
                }
            }
            await using (mutation)
            await using (var transaction = context.Database.CurrentTransaction is null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null)
            {
                foreach (var snapshot in activeSnapshots)
                {
                    Validate(snapshot, out var kind, out var state);
                    var checkpoint = await context.CommentRelationshipCheckpoints
                        .SingleOrDefaultAsync(item => item.StreamId == snapshot.StreamId, cancellationToken);
                    if (checkpoint is null)
                    {
                        checkpoint = CommentRelationshipCheckpoint.Create(snapshot.StreamId,
                            snapshot.OwnerUserId, snapshot.ViewerUserId, kind, observed);
                        await context.CommentRelationshipCheckpoints.AddAsync(checkpoint, cancellationToken);
                    }
                    else if (checkpoint.Classify(snapshot.StreamId, snapshot.OwnerUserId,
                        snapshot.ViewerUserId, kind, Math.Max(snapshot.StreamVersion,
                        checkpoint.LastAppliedVersion + 1)) == CommentProjectionVersionDisposition.IdentityMismatch)
                    {
                        throw new InvalidOperationException("An authoritative snapshot changed a stream identity.");
                    }

                    if (snapshot.StreamVersion < checkpoint.LastAppliedVersion)
                        continue;

                    checkpoint.Reconcile(snapshot.LastEventId, snapshot.StreamVersion, snapshot.AsOfUtc, observed);
                    await UpsertEdgeAsync(snapshot.OwnerUserId, snapshot.ViewerUserId, kind, state,
                        snapshot.StreamId, snapshot.LastEventId, snapshot.StreamVersion,
                        snapshot.AsOfUtc, observed, cancellationToken);
                    var quarantine = await context.CommentRelationshipEventQuarantines
                        .Where(item => item.StreamId == snapshot.StreamId && item.ResolvedAtUtc == null)
                        .Take(100).ToListAsync(cancellationToken);
                    foreach (var item in quarantine)
                        item.Resolve("authoritative_snapshot", observed);
                }
                await context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, mutation, cancellationToken);
            }
            return activeSnapshots.Count;
        }

        private async Task UpsertEdgeAsync(int owner, int viewer, string kind, string state,
            Guid stream, Guid eventId, long version, DateTime sourceAt, DateTime observed,
            CancellationToken cancellationToken)
        {
            var edge = await context.CommentRelationshipEdges.SingleOrDefaultAsync(item =>
                item.OwnerUserId == owner && item.ViewerUserId == viewer
                && item.RelationshipKind == kind, cancellationToken);
            if (edge is null)
                await context.CommentRelationshipEdges.AddAsync(CommentRelationshipEdge.Create(
                    owner, viewer, kind, state, stream, eventId, version, sourceAt, observed), cancellationToken);
            else
                edge.Apply(stream, eventId, state, version, sourceAt, observed);
        }

        private async Task FailCloseEdgeAsync(RelationshipStateChanged message, string kind,
            DateTime observed, CancellationToken cancellationToken)
        {
            var edge = await context.CommentRelationshipEdges.SingleOrDefaultAsync(item =>
                item.OwnerUserId == message.OwnerUserId && item.ViewerUserId == message.ViewerUserId
                && item.RelationshipKind == kind, cancellationToken);
            if (edge is not null)
                edge.FailClosed(
                    kind == RelationshipKinds.Block ? RelationshipStates.Blocked : RelationshipStates.Revoked,
                    observed);
        }

        private async Task<CommentRelationshipProjectionResult> QuarantineInvalidAsync(
            RelationshipStateChanged message, DateTime observed, CancellationToken cancellationToken)
        {
            var eventId = message.EventId == Guid.Empty ? Guid.NewGuid() : message.EventId;
            var streamId = message.StreamId == Guid.Empty ? Guid.NewGuid() : message.StreamId;
            if (!await context.CommentRelationshipEventQuarantines.AnyAsync(item => item.EventId == eventId,
                cancellationToken))
            {
                await context.CommentRelationshipEventQuarantines.AddAsync(
                    CommentRelationshipEventQuarantine.Create(eventId, streamId, message.OwnerUserId,
                        message.ViewerUserId, message.RelationshipKind, message.State,
                        message.StreamVersion, 1, "invalid_contract", message.OccurredAtUtc,
                        observed), cancellationToken);
            }
            await DegradeGlobalAsync("invalid_contract", observed, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            telemetry.Event("other", "invalid");
            return new(CommentRelationshipProjectionOutcome.InvalidQuarantined,
                message.StreamId, 0, 1);
        }

        private async Task AddQuarantineAsync(RelationshipStateChanged message, string kind,
            string state, long expected, string reason, DateTime observed,
            CancellationToken cancellationToken)
        {
            if (await context.CommentRelationshipEventQuarantines.AnyAsync(
                item => item.EventId == message.EventId, cancellationToken))
                return;
            await context.CommentRelationshipEventQuarantines.AddAsync(
                CommentRelationshipEventQuarantine.Create(message.EventId, message.StreamId,
                    message.OwnerUserId, message.ViewerUserId, kind, state, message.StreamVersion,
                    expected, reason, message.OccurredAtUtc, observed), cancellationToken);
        }

        private async Task DegradeGlobalAsync(string reason, DateTime observed,
            CancellationToken cancellationToken)
        {
            var projection = await context.CommentAudienceProjectionStates.SingleOrDefaultAsync(
                item => item.Id == CommentAudienceProjectionState.SingletonId, cancellationToken);
            if (projection is null)
            {
                projection = CommentAudienceProjectionState.Create(observed);
                await context.CommentAudienceProjectionStates.AddAsync(projection, cancellationToken);
            }
            projection.Degrade(reason, observed);
        }

        private static bool TryValidate(RelationshipStateChanged value, out string kind, out string state)
        {
            kind = value.RelationshipKind?.Trim().ToLowerInvariant() ?? string.Empty;
            state = value.State?.Trim().ToLowerInvariant() ?? string.Empty;
            return value.EventId != Guid.Empty && value.StreamId != Guid.Empty
                && value.StreamVersion > 0 && value.OwnerUserId > 0 && value.ViewerUserId > 0
                && value.OwnerUserId != value.ViewerUserId && value.OccurredAtUtc != default
                && RelationshipKinds.IsSupported(kind) && RelationshipStates.IsSupported(state)
                && (kind == RelationshipKinds.Block
                    ? state is RelationshipStates.Blocked or RelationshipStates.Revoked
                    : state is RelationshipStates.Active or RelationshipStates.Revoked);
        }

        private static void Validate(RelationshipStateSnapshot value, out string kind, out string state)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            kind = value.RelationshipKind?.Trim().ToLowerInvariant() ?? string.Empty;
            state = value.State?.Trim().ToLowerInvariant() ?? string.Empty;
            if (value.Cursor <= 0 || value.StreamId == Guid.Empty
                || value.LastEventId == Guid.Empty || value.StreamVersion <= 0 || value.OwnerUserId <= 0
                || value.ViewerUserId <= 0 || value.OwnerUserId == value.ViewerUserId
                || value.AsOfUtc == default || !RelationshipKinds.IsSupported(kind)
                || !RelationshipStates.IsSupported(state)
                || (kind == RelationshipKinds.Block
                    ? state is not (RelationshipStates.Blocked or RelationshipStates.Revoked)
                    : state is not (RelationshipStates.Active or RelationshipStates.Revoked)))
                throw new InvalidOperationException("Following returned an invalid relationship snapshot row.");
        }

        private static DateTime Utc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ when value == default => throw new ArgumentException("A timestamp is required."),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private static async Task CommitAsync(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
            ICommentUserMutationScope? mutation,
            CancellationToken cancellationToken)
        {
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            if (mutation is not null)
                await mutation.CommitAsync(cancellationToken);
        }
    }
}
