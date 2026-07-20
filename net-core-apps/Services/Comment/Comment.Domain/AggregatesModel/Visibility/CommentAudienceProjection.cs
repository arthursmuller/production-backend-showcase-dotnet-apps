using System;

namespace Comment.Domain.AggregatesModel.Visibility
{
    public static class CommentProjectionStates
    {
        public const string Uninitialized = "uninitialized";
        public const string Healthy = "healthy";
        public const string Gap = "gap";
        public const string Quarantined = "quarantined";
        public const string Degraded = "degraded";
    }

    public enum CommentProjectionVersionDisposition
    {
        Next,
        StaleOrDuplicate,
        Gap,
        IdentityMismatch,
    }

    public sealed class CommentRelationshipCheckpoint : ICommentConcurrencyTracked
    {
        public Guid StreamId { get; private set; }
        public int OwnerUserId { get; private set; }
        public int ViewerUserId { get; private set; }
        public string RelationshipKind { get; private set; } = string.Empty;
        public long LastAppliedVersion { get; private set; }
        public long HighestObservedVersion { get; private set; }
        public string Status { get; private set; } = CommentProjectionStates.Uninitialized;
        public long? GapFromVersion { get; private set; }
        public long? GapToVersion { get; private set; }
        public Guid? LastEventId { get; private set; }
        public DateTime? LastSourceOccurredAtUtc { get; private set; }
        public DateTime LastConfirmedAtUtc { get; private set; }
        public DateTime? LastReconciledAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private CommentRelationshipCheckpoint() { }

        public static CommentRelationshipCheckpoint Create(
            Guid streamId,
            int ownerUserId,
            int viewerUserId,
            string relationshipKind,
            DateTime observedAtUtc)
        {
            ValidateIdentity(streamId, ownerUserId, viewerUserId, relationshipKind);
            var observed = Utc(observedAtUtc);
            return new CommentRelationshipCheckpoint
            {
                StreamId = streamId,
                OwnerUserId = ownerUserId,
                ViewerUserId = viewerUserId,
                RelationshipKind = relationshipKind.Trim().ToLowerInvariant(),
                Status = CommentProjectionStates.Uninitialized,
                LastConfirmedAtUtc = observed,
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                Version = 1,
            };
        }

        public CommentProjectionVersionDisposition Classify(
            Guid streamId,
            int ownerUserId,
            int viewerUserId,
            string relationshipKind,
            long incomingVersion)
        {
            if (StreamId != streamId || OwnerUserId != ownerUserId || ViewerUserId != viewerUserId
                || !string.Equals(RelationshipKind, relationshipKind?.Trim(), StringComparison.OrdinalIgnoreCase))
                return CommentProjectionVersionDisposition.IdentityMismatch;
            if (incomingVersion <= LastAppliedVersion)
                return CommentProjectionVersionDisposition.StaleOrDuplicate;
            return incomingVersion == LastAppliedVersion + 1
                ? CommentProjectionVersionDisposition.Next
                : CommentProjectionVersionDisposition.Gap;
        }

        public void Apply(Guid eventId, long sourceVersion, DateTime sourceOccurredAtUtc, DateTime observedAtUtc)
        {
            if (eventId == Guid.Empty || sourceVersion != LastAppliedVersion + 1)
                throw new InvalidOperationException("Only the next valid relationship event can be applied.");
            LastAppliedVersion = sourceVersion;
            HighestObservedVersion = Math.Max(HighestObservedVersion, sourceVersion);
            Status = CommentProjectionStates.Healthy;
            GapFromVersion = null;
            GapToVersion = null;
            LastEventId = eventId;
            LastSourceOccurredAtUtc = Utc(sourceOccurredAtUtc);
            LastConfirmedAtUtc = Utc(observedAtUtc);
            UpdatedAtUtc = LastConfirmedAtUtc;
        }

        public void Gap(long receivedVersion, DateTime observedAtUtc)
        {
            if (receivedVersion <= LastAppliedVersion + 1)
                throw new ArgumentOutOfRangeException(nameof(receivedVersion));
            HighestObservedVersion = Math.Max(HighestObservedVersion, receivedVersion);
            Status = CommentProjectionStates.Gap;
            GapFromVersion ??= LastAppliedVersion + 1;
            GapToVersion = Math.Max(GapToVersion ?? 0, receivedVersion - 1);
            UpdatedAtUtc = Utc(observedAtUtc);
        }

        public void Quarantine(long receivedVersion, DateTime observedAtUtc)
        {
            HighestObservedVersion = Math.Max(HighestObservedVersion, receivedVersion);
            Status = CommentProjectionStates.Quarantined;
            UpdatedAtUtc = Utc(observedAtUtc);
        }

        public void Reconcile(Guid lastEventId, long sourceVersion, DateTime sourceOccurredAtUtc, DateTime observedAtUtc)
        {
            if (lastEventId == Guid.Empty || sourceVersion <= 0)
                throw new ArgumentException("A valid authoritative stream position is required.");
            if (sourceVersion < LastAppliedVersion)
                return;
            LastAppliedVersion = sourceVersion;
            HighestObservedVersion = Math.Max(HighestObservedVersion, sourceVersion);
            Status = CommentProjectionStates.Healthy;
            GapFromVersion = null;
            GapToVersion = null;
            LastEventId = lastEventId;
            LastSourceOccurredAtUtc = Utc(sourceOccurredAtUtc);
            LastConfirmedAtUtc = Utc(observedAtUtc);
            LastReconciledAtUtc = LastConfirmedAtUtc;
            UpdatedAtUtc = LastConfirmedAtUtc;
        }

        private static void ValidateIdentity(Guid streamId, int ownerUserId, int viewerUserId, string kind)
        {
            if (streamId == Guid.Empty || ownerUserId <= 0 || viewerUserId <= 0 || ownerUserId == viewerUserId
                || string.IsNullOrWhiteSpace(kind) || kind.Length > 32)
                throw new ArgumentException("The relationship stream identity is invalid.");
        }

        private static DateTime Utc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ when value == default => throw new ArgumentException("A timestamp is required."),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public sealed class CommentRelationshipEdge : ICommentConcurrencyTracked
    {
        public long Id { get; private set; }
        public int OwnerUserId { get; private set; }
        public int ViewerUserId { get; private set; }
        public string RelationshipKind { get; private set; } = string.Empty;
        public string State { get; private set; } = string.Empty;
        public Guid SourceStreamId { get; private set; }
        public Guid LastEventId { get; private set; }
        public long SourceVersion { get; private set; }
        public DateTime SourceOccurredAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private CommentRelationshipEdge() { }

        public static CommentRelationshipEdge Create(int owner, int viewer, string kind, string state,
            Guid stream, Guid eventId, long sourceVersion, DateTime sourceAt, DateTime observedAt)
        {
            if (owner <= 0 || viewer <= 0 || owner == viewer || stream == Guid.Empty || eventId == Guid.Empty
                || sourceVersion <= 0 || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(state))
                throw new ArgumentException("The relationship edge is invalid.");
            return new CommentRelationshipEdge
            {
                OwnerUserId = owner,
                ViewerUserId = viewer,
                RelationshipKind = kind.Trim().ToLowerInvariant(),
                State = state.Trim().ToLowerInvariant(),
                SourceStreamId = stream,
                LastEventId = eventId,
                SourceVersion = sourceVersion,
                SourceOccurredAtUtc = Utc(sourceAt),
                CreatedAtUtc = Utc(observedAt),
                UpdatedAtUtc = Utc(observedAt),
                Version = 1,
            };
        }

        public void Apply(Guid stream, Guid eventId, string state, long sourceVersion, DateTime sourceAt, DateTime observedAt)
        {
            if (SourceStreamId != stream || eventId == Guid.Empty || sourceVersion < SourceVersion)
                throw new InvalidOperationException("The relationship edge stream position is invalid.");
            LastEventId = eventId;
            State = state.Trim().ToLowerInvariant();
            SourceVersion = sourceVersion;
            SourceOccurredAtUtc = Utc(sourceAt);
            UpdatedAtUtc = Utc(observedAt);
        }

        public void FailClosed(string state, DateTime observedAt)
        {
            State = state;
            UpdatedAtUtc = Utc(observedAt);
        }

        private static DateTime Utc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ when value == default => throw new ArgumentException("A timestamp is required."),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public sealed class CommentRelationshipEventQuarantine
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

        private CommentRelationshipEventQuarantine() { }

        public static CommentRelationshipEventQuarantine Create(Guid eventId, Guid streamId, int owner, int viewer,
            string kind, string state, long receivedVersion, long expectedVersion, string reason,
            DateTime sourceAt, DateTime recordedAt) => new()
            {
                EventId = eventId,
                StreamId = streamId,
                OwnerUserId = owner,
                ViewerUserId = viewer,
                RelationshipKind = Safe(kind, "invalid", 32),
                State = Safe(state, "invalid", 24),
                ReceivedVersion = Math.Max(0, receivedVersion),
                ExpectedVersion = Math.Max(1, expectedVersion),
                ReasonCode = Safe(reason, "invalid", 64),
                SourceOccurredAtUtc = sourceAt == default ? DateTime.UnixEpoch : sourceAt.ToUniversalTime(),
                RecordedAtUtc = recordedAt.ToUniversalTime(),
            };

        public void Resolve(string resolution, DateTime resolvedAtUtc)
        {
            if (ResolvedAtUtc.HasValue)
                return;
            Resolution = Safe(resolution, "reconciled", 64);
            ResolvedAtUtc = resolvedAtUtc.ToUniversalTime();
        }

        private static string Safe(string? value, string fallback, int maximum)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim().ToLowerInvariant();
            return normalized.Length <= maximum ? normalized : normalized[..maximum];
        }
    }

    public sealed class CommentAudienceProjectionState : ICommentConcurrencyTracked
    {
        public const int SingletonId = 1;
        public int Id { get; private set; }
        public string Status { get; private set; } = CommentProjectionStates.Uninitialized;
        public Guid? CycleId { get; private set; }
        public bool CycleInProgress { get; private set; }
        public long NextCursor { get; private set; }
        public long? SnapshotHighWatermark { get; private set; }
        public DateTime? SnapshotAsOfUtc { get; private set; }
        public DateTime? CycleStartedAtUtc { get; private set; }
        public DateTime? LastCompletedAtUtc { get; private set; }
        public string? LastErrorCode { get; private set; }
        public string? LeaseOwner { get; private set; }
        public Guid? LeaseToken { get; private set; }
        public DateTime? LeaseExpiresAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private CommentAudienceProjectionState() { }

        public static CommentAudienceProjectionState Create(DateTime observedAtUtc) => new()
        {
            Id = SingletonId,
            Status = CommentProjectionStates.Uninitialized,
            CreatedAtUtc = observedAtUtc.ToUniversalTime(),
            UpdatedAtUtc = observedAtUtc.ToUniversalTime(),
            Version = 1,
        };

        public Guid? TryLease(string owner, DateTime observedAtUtc, TimeSpan duration)
        {
            var observed = observedAtUtc.ToUniversalTime();
            if (string.IsNullOrWhiteSpace(owner) || duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(5))
                throw new ArgumentException("The projection lease is invalid.");
            if (LeaseToken.HasValue && LeaseExpiresAtUtc > observed)
                return null;
            LeaseOwner = owner.Length > 128 ? owner[..128] : owner;
            LeaseToken = Guid.NewGuid();
            LeaseExpiresAtUtc = observed.Add(duration);
            UpdatedAtUtc = observed;
            return LeaseToken;
        }

        public void BeginCycle(Guid token, DateTime observedAtUtc)
        {
            RequireLease(token, observedAtUtc);
            if (CycleInProgress)
                return;
            CycleId = Guid.NewGuid();
            CycleInProgress = true;
            NextCursor = 0;
            SnapshotHighWatermark = null;
            SnapshotAsOfUtc = null;
            CycleStartedAtUtc = observedAtUtc.ToUniversalTime();
            LastErrorCode = null;
            UpdatedAtUtc = observedAtUtc.ToUniversalTime();
        }

        public void ApplyPage(Guid token, long highWatermark, DateTime snapshotAsOfUtc, long nextCursor,
            bool hasMore, DateTime observedAtUtc)
        {
            RequireLease(token, observedAtUtc);
            if (!CycleInProgress || highWatermark < 0 || nextCursor < NextCursor || nextCursor > highWatermark)
                throw new InvalidOperationException("The fixed relationship snapshot boundary is invalid.");
            if (!SnapshotHighWatermark.HasValue)
            {
                SnapshotHighWatermark = highWatermark;
                SnapshotAsOfUtc = snapshotAsOfUtc.ToUniversalTime();
            }
            else if (SnapshotHighWatermark != highWatermark || SnapshotAsOfUtc != snapshotAsOfUtc.ToUniversalTime())
                throw new InvalidOperationException("The relationship snapshot boundary moved.");
            if ((hasMore && nextCursor >= highWatermark) || (!hasMore && nextCursor != highWatermark))
                throw new InvalidOperationException("The relationship snapshot page is incomplete.");
            NextCursor = nextCursor;
            if (!hasMore)
            {
                CycleInProgress = false;
                Status = CommentProjectionStates.Healthy;
                LastCompletedAtUtc = observedAtUtc.ToUniversalTime();
            }
            ReleaseLease();
            UpdatedAtUtc = observedAtUtc.ToUniversalTime();
        }

        public void Degrade(string errorCode, DateTime observedAtUtc)
        {
            Status = CommentProjectionStates.Degraded;
            CycleInProgress = false;
            LastErrorCode = errorCode?.Trim().ToLowerInvariant() ?? "projection_failed";
            ReleaseLease();
            UpdatedAtUtc = observedAtUtc.ToUniversalTime();
        }

        public bool IsCompleteAndFresh(DateTime atUtc, TimeSpan maximumAge) =>
            Status == CommentProjectionStates.Healthy && LastCompletedAtUtc.HasValue
            && LastCompletedAtUtc >= atUtc.ToUniversalTime() - maximumAge;

        private void RequireLease(Guid token, DateTime observedAtUtc)
        {
            if (token == Guid.Empty || LeaseToken != token || LeaseExpiresAtUtc <= observedAtUtc.ToUniversalTime())
                throw new InvalidOperationException("The projection lease is missing or expired.");
        }

        private void ReleaseLease()
        {
            LeaseOwner = null;
            LeaseToken = null;
            LeaseExpiresAtUtc = null;
        }
    }
}
