using System;

namespace Feed.Domain.AggregatesModel
{
    public static class AudienceProjectionStates
    {
        public const string Uninitialized = "uninitialized";
        public const string Healthy = "healthy";
        public const string Gap = "gap";
        public const string Quarantined = "quarantined";
    }

    public static class AudienceBootstrapStates
    {
        public const string Uninitialized = "uninitialized";
        public const string Healthy = "healthy";
        public const string Degraded = "degraded";
    }

    /// <summary>
    /// Durable singleton proving that Feed has consumed one fixed, complete authority snapshot.
    /// Non-owner authorization must not infer the absence of a relationship or block without it.
    /// </summary>
    public sealed class FeedAudienceBootstrapState
    {
        public const int SingletonId = 1;

        public int Id { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public Guid? CycleId { get; private set; }
        public bool CycleInProgress { get; private set; }
        public long NextCursor { get; private set; }
        public long? SnapshotHighWatermark { get; private set; }
        public DateTime? SnapshotAsOfUtc { get; private set; }
        public DateTime? CycleStartedAtUtc { get; private set; }
        public DateTime? LastPageAtUtc { get; private set; }
        public DateTime? LastCompletedAtUtc { get; private set; }
        public string? LastErrorCode { get; private set; }
        public string? LeaseOwner { get; private set; }
        public Guid? LeaseToken { get; private set; }
        public DateTime? LeaseExpiresAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedAudienceBootstrapState() { }

        public static FeedAudienceBootstrapState Create(DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            return new FeedAudienceBootstrapState
            {
                Id = SingletonId,
                Status = AudienceBootstrapStates.Uninitialized,
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                Version = 1,
            };
        }

        public Guid? TryLease(
            string owner,
            DateTime observedAtUtc,
            TimeSpan duration)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(5))
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (LeaseToken.HasValue && LeaseExpiresAtUtc > observed)
                return null;

            LeaseOwner = FeedGuard.Required(
                owner,
                FeedSchemaLimits.LeaseOwnerLength,
                nameof(owner));
            LeaseToken = Guid.NewGuid();
            LeaseExpiresAtUtc = observed.Add(duration);
            UpdatedAtUtc = observed;
            Version++;
            return LeaseToken;
        }

        public void BeginCycle(Guid leaseToken, DateTime observedAtUtc)
        {
            var observed = RequireLease(leaseToken, observedAtUtc);
            if (CycleInProgress)
                return;

            CycleId = Guid.NewGuid();
            CycleInProgress = true;
            NextCursor = 0;
            SnapshotHighWatermark = null;
            SnapshotAsOfUtc = null;
            CycleStartedAtUtc = observed;
            LastPageAtUtc = null;
            LastErrorCode = null;
            UpdatedAtUtc = observed;
            Version++;
        }

        public void ApplyPage(
            Guid leaseToken,
            long snapshotHighWatermark,
            DateTime snapshotAsOfUtc,
            long nextCursor,
            bool hasMore,
            DateTime observedAtUtc)
        {
            var observed = RequireLease(leaseToken, observedAtUtc);
            if (!CycleInProgress)
                throw new InvalidOperationException("An audience bootstrap cycle is not active.");
            if (snapshotHighWatermark < 0
                || nextCursor < NextCursor
                || nextCursor > snapshotHighWatermark)
            {
                throw new InvalidOperationException("The authority snapshot cursor moved outside its fixed boundary.");
            }

            var snapshotAsOf = FeedGuard.Utc(snapshotAsOfUtc, nameof(snapshotAsOfUtc));
            if (!SnapshotHighWatermark.HasValue)
            {
                SnapshotHighWatermark = snapshotHighWatermark;
                SnapshotAsOfUtc = snapshotAsOf;
            }
            else if (SnapshotHighWatermark != snapshotHighWatermark
                || SnapshotAsOfUtc != snapshotAsOf)
            {
                throw new InvalidOperationException("The authority changed a fixed bootstrap boundary.");
            }

            if (hasMore && nextCursor >= snapshotHighWatermark)
                throw new InvalidOperationException("A complete snapshot cannot advertise another page.");
            if (!hasMore && nextCursor != snapshotHighWatermark)
                throw new InvalidOperationException("The final bootstrap page did not reach its fixed boundary.");

            NextCursor = nextCursor;
            LastPageAtUtc = observed;
            LastErrorCode = null;
            if (!hasMore)
            {
                Status = AudienceBootstrapStates.Healthy;
                CycleInProgress = false;
                LastCompletedAtUtc = observed;
            }
            ReleaseLease();
            UpdatedAtUtc = observed;
            Version++;
        }

        public void RecordFailure(
            Guid leaseToken,
            string errorCode,
            DateTime observedAtUtc)
        {
            var observed = RequireLease(leaseToken, observedAtUtc);
            LastErrorCode = FeedGuard.Normalized(
                errorCode,
                FeedSchemaLimits.ErrorCodeLength,
                nameof(errorCode));
            ReleaseLease();
            UpdatedAtUtc = observed;
            Version++;
        }

        public void MarkDegraded(string errorCode, DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Status = AudienceBootstrapStates.Degraded;
            CycleInProgress = false;
            LastErrorCode = FeedGuard.Normalized(
                errorCode,
                FeedSchemaLimits.ErrorCodeLength,
                nameof(errorCode));
            ReleaseLease();
            UpdatedAtUtc = observed;
            Version++;
        }

        public bool IsCompleteAndFresh(DateTime evaluatedAtUtc, TimeSpan maximumAge)
        {
            if (Status != AudienceBootstrapStates.Healthy
                || !LastCompletedAtUtc.HasValue
                || !SnapshotAsOfUtc.HasValue
                || maximumAge <= TimeSpan.Zero)
            {
                return false;
            }
            var evaluated = FeedGuard.Utc(evaluatedAtUtc, nameof(evaluatedAtUtc));
            var cutoff = evaluated - maximumAge;
            // SnapshotAsOfUtc is the immutable source boundary timestamp used to prove that
            // every page belonged to the same authority snapshot. It may legitimately be old
            // (or UnixEpoch for an empty authority), so local successful completion is the
            // freshness clock. Live relationship checkpoints are freshness-gated separately.
            return LastCompletedAtUtc >= cutoff;
        }

        private DateTime RequireLease(Guid leaseToken, DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            if (leaseToken == Guid.Empty
                || LeaseToken != leaseToken
                || LeaseExpiresAtUtc <= observed)
            {
                throw new InvalidOperationException("The audience bootstrap lease is missing, stale, or expired.");
            }
            return observed;
        }

        private void ReleaseLease()
        {
            LeaseOwner = null;
            LeaseToken = null;
            LeaseExpiresAtUtc = null;
        }
    }

    public enum ProjectionVersionDisposition
    {
        Next,
        StaleOrDuplicate,
        Gap,
        StreamIdentityMismatch,
    }

    public sealed class FeedRelationshipCheckpoint
    {
        public Guid StreamId { get; private set; }
        public int OwnerUserId { get; private set; }
        public int ViewerUserId { get; private set; }
        public string RelationshipKind { get; private set; } = string.Empty;
        public long LastAppliedVersion { get; private set; }
        public long HighestObservedVersion { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public long? GapFromVersion { get; private set; }
        public long? GapToVersion { get; private set; }
        public Guid? LastEventId { get; private set; }
        public DateTime? LastSourceOccurredAtUtc { get; private set; }
        public DateTime LastConfirmedAtUtc { get; private set; }
        public DateTime? LastReconciledAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedRelationshipCheckpoint() { }

        public static FeedRelationshipCheckpoint Create(
            Guid streamId,
            int ownerUserId,
            int viewerUserId,
            string relationshipKind,
            DateTime observedAtUtc)
        {
            ValidateIdentity(streamId, ownerUserId, viewerUserId);
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            return new FeedRelationshipCheckpoint
            {
                StreamId = streamId,
                OwnerUserId = ownerUserId,
                ViewerUserId = viewerUserId,
                RelationshipKind = FeedGuard.Normalized(
                    relationshipKind,
                    FeedSchemaLimits.RelationshipKindLength,
                    nameof(relationshipKind)),
                Status = AudienceProjectionStates.Uninitialized,
                LastConfirmedAtUtc = observed,
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                Version = 1,
            };
        }

        public ProjectionVersionDisposition Classify(
            Guid streamId,
            int ownerUserId,
            int viewerUserId,
            string relationshipKind,
            long incomingVersion)
        {
            if (StreamId != streamId
                || OwnerUserId != ownerUserId
                || ViewerUserId != viewerUserId
                || !string.Equals(
                    RelationshipKind,
                    relationshipKind?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return ProjectionVersionDisposition.StreamIdentityMismatch;
            }
            if (incomingVersion <= LastAppliedVersion)
                return ProjectionVersionDisposition.StaleOrDuplicate;
            if (incomingVersion == LastAppliedVersion + 1)
                return ProjectionVersionDisposition.Next;
            return ProjectionVersionDisposition.Gap;
        }

        public void MarkApplied(
            Guid eventId,
            long sourceVersion,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("An event ID is required.", nameof(eventId));
            if (sourceVersion != LastAppliedVersion + 1)
                throw new InvalidOperationException("Only the next checkpoint version can be applied.");

            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            LastAppliedVersion = sourceVersion;
            HighestObservedVersion = Math.Max(HighestObservedVersion, sourceVersion);
            Status = AudienceProjectionStates.Healthy;
            GapFromVersion = null;
            GapToVersion = null;
            LastEventId = eventId;
            LastSourceOccurredAtUtc = FeedGuard.Utc(
                sourceOccurredAtUtc,
                nameof(sourceOccurredAtUtc));
            LastConfirmedAtUtc = observed;
            UpdatedAtUtc = observed;
            Version++;
        }

        public void MarkGap(long receivedVersion, DateTime observedAtUtc)
        {
            if (receivedVersion <= LastAppliedVersion + 1)
                throw new ArgumentOutOfRangeException(nameof(receivedVersion));
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            HighestObservedVersion = Math.Max(HighestObservedVersion, receivedVersion);
            Status = AudienceProjectionStates.Gap;
            GapFromVersion ??= LastAppliedVersion + 1;
            GapToVersion = Math.Max(GapToVersion ?? 0, receivedVersion - 1);
            UpdatedAtUtc = observed;
            Version++;
        }

        public void MarkQuarantined(long receivedVersion, DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            HighestObservedVersion = Math.Max(HighestObservedVersion, receivedVersion);
            Status = AudienceProjectionStates.Quarantined;
            UpdatedAtUtc = observed;
            Version++;
        }

        public void Reconcile(
            Guid lastEventId,
            long sourceVersion,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            if (sourceVersion < LastAppliedVersion)
                return;
            if (lastEventId == Guid.Empty)
                throw new ArgumentException("A last event ID is required.", nameof(lastEventId));
            FeedGuard.Positive(sourceVersion, nameof(sourceVersion));
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            LastAppliedVersion = sourceVersion;
            HighestObservedVersion = Math.Max(HighestObservedVersion, sourceVersion);
            Status = AudienceProjectionStates.Healthy;
            GapFromVersion = null;
            GapToVersion = null;
            LastEventId = lastEventId;
            LastSourceOccurredAtUtc = FeedGuard.Utc(
                sourceOccurredAtUtc,
                nameof(sourceOccurredAtUtc));
            LastConfirmedAtUtc = observed;
            LastReconciledAtUtc = observed;
            UpdatedAtUtc = observed;
            Version++;
        }

        public bool IsFresh(DateTime evaluatedAtUtc, TimeSpan maximumAge)
        {
            if (Status != AudienceProjectionStates.Healthy || maximumAge <= TimeSpan.Zero)
                return false;
            var evaluated = FeedGuard.Utc(evaluatedAtUtc, nameof(evaluatedAtUtc));
            return LastConfirmedAtUtc >= evaluated - maximumAge;
        }

        private static void ValidateIdentity(
            Guid streamId,
            int ownerUserId,
            int viewerUserId)
        {
            if (streamId == Guid.Empty)
                throw new ArgumentException("A stream ID is required.", nameof(streamId));
            FeedGuard.Positive(ownerUserId, nameof(ownerUserId));
            FeedGuard.Positive(viewerUserId, nameof(viewerUserId));
            if (ownerUserId == viewerUserId)
                throw new ArgumentException("Relationship checkpoints must be directional.");
        }
    }

    public sealed class FeedRelationshipEventQuarantine
    {
        public long Id { get; private set; }
        public Guid EventId { get; private set; }
        public Guid StreamId { get; private set; }
        public int OwnerUserId { get; private set; }
        public int ViewerUserId { get; private set; }
        public string RelationshipKind { get; private set; } = string.Empty;
        public string State { get; private set; } = string.Empty;
        public long ReceivedVersion { get; private set; }
        public long ExpectedVersion { get; private set; }
        public string ReasonCode { get; private set; } = string.Empty;
        public DateTime SourceOccurredAtUtc { get; private set; }
        public DateTime RecordedAtUtc { get; private set; }
        public DateTime? ResolvedAtUtc { get; private set; }
        public string? Resolution { get; private set; }

        private FeedRelationshipEventQuarantine() { }

        public static FeedRelationshipEventQuarantine Create(
            Guid eventId,
            Guid streamId,
            int ownerUserId,
            int viewerUserId,
            string relationshipKind,
            string state,
            long receivedVersion,
            long expectedVersion,
            string reasonCode,
            DateTime sourceOccurredAtUtc,
            DateTime recordedAtUtc)
        {
            if (eventId == Guid.Empty || streamId == Guid.Empty)
                throw new ArgumentException("Event and stream IDs are required.");
            FeedGuard.Positive(receivedVersion, nameof(receivedVersion));
            FeedGuard.Positive(expectedVersion, nameof(expectedVersion));
            var recorded = FeedGuard.Utc(recordedAtUtc, nameof(recordedAtUtc));
            return new FeedRelationshipEventQuarantine
            {
                EventId = eventId,
                StreamId = streamId,
                OwnerUserId = ownerUserId,
                ViewerUserId = viewerUserId,
                RelationshipKind = FeedGuard.Normalized(
                    relationshipKind,
                    FeedSchemaLimits.RelationshipKindLength,
                    nameof(relationshipKind)),
                State = FeedGuard.Normalized(state, FeedSchemaLimits.StateLength, nameof(state)),
                ReceivedVersion = receivedVersion,
                ExpectedVersion = expectedVersion,
                ReasonCode = FeedGuard.Normalized(
                    reasonCode,
                    FeedSchemaLimits.ErrorCodeLength,
                    nameof(reasonCode)),
                SourceOccurredAtUtc = FeedGuard.Utc(
                    sourceOccurredAtUtc,
                    nameof(sourceOccurredAtUtc)),
                RecordedAtUtc = recorded,
            };
        }

        public void Resolve(string resolution, DateTime resolvedAtUtc)
        {
            if (ResolvedAtUtc.HasValue)
                return;
            Resolution = FeedGuard.Normalized(
                resolution,
                FeedSchemaLimits.ErrorCodeLength,
                nameof(resolution));
            ResolvedAtUtc = FeedGuard.Utc(resolvedAtUtc, nameof(resolvedAtUtc));
        }
    }
}
