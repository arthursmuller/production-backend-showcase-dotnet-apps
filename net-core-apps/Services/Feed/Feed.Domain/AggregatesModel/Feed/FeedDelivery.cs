using System;

namespace Feed.Domain.AggregatesModel
{
    public static class FeedDeliveryModes
    {
        public const string Push = "push";
        public const string Pull = "pull";

        public static string Normalize(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is Push or Pull
                ? normalized
                : throw new ArgumentException("Delivery mode must be push or pull.", nameof(value));
        }
    }

    public static class FeedActivityLifecycle
    {
        public const string Active = "active";
        public const string Revoked = "revoked";
        public const string Deleted = "deleted";

        public static string Normalize(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is Active or Revoked or Deleted
                ? normalized
                : throw new ArgumentException("Unsupported activity lifecycle state.", nameof(value));
        }
    }

    public static class FeedWorkStates
    {
        public const string Pending = "pending";
        public const string Processing = "processing";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public static class FeedWorkKinds
    {
        public const string Fanout = "fanout";
        public const string Replay = "replay";

        public static string Normalize(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is Fanout or Replay
                ? normalized
                : throw new ArgumentException("Unsupported feed work kind.", nameof(value));
        }
    }

    public static class FeedRevocationKinds
    {
        public const string Activity = "activity";
        public const string Relationship = "relationship";
        public const string Block = "block";
        public const string FriendsOfFriends = "friends_of_friends";
    }

    public sealed class FeedPublisherState
    {
        public int PublisherUserId { get; private set; }
        public string DeliveryMode { get; private set; } = string.Empty;
        public long AudienceCount { get; private set; }
        public DateTime LastEvaluatedAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedPublisherState() { }

        public static FeedPublisherState Create(
            int publisherUserId,
            long audienceCount,
            int enterPullThreshold,
            int exitPullThreshold,
            DateTime observedAtUtc)
        {
            ValidateThresholds(enterPullThreshold, exitPullThreshold);
            FeedGuard.Positive(publisherUserId, nameof(publisherUserId));
            if (audienceCount < 0)
                throw new ArgumentOutOfRangeException(nameof(audienceCount));
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            return new FeedPublisherState
            {
                PublisherUserId = publisherUserId,
                AudienceCount = audienceCount,
                DeliveryMode = audienceCount >= enterPullThreshold
                    ? FeedDeliveryModes.Pull
                    : FeedDeliveryModes.Push,
                LastEvaluatedAtUtc = observed,
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                Version = 1,
            };
        }

        public bool ObserveAudienceCount(
            long audienceCount,
            int enterPullThreshold,
            int exitPullThreshold,
            DateTime observedAtUtc)
        {
            ValidateThresholds(enterPullThreshold, exitPullThreshold);
            if (audienceCount < 0)
                throw new ArgumentOutOfRangeException(nameof(audienceCount));
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            var nextMode = DeliveryMode;
            if (DeliveryMode == FeedDeliveryModes.Push && audienceCount >= enterPullThreshold)
                nextMode = FeedDeliveryModes.Pull;
            else if (DeliveryMode == FeedDeliveryModes.Pull && audienceCount <= exitPullThreshold)
                nextMode = FeedDeliveryModes.Push;

            var changed = AudienceCount != audienceCount || DeliveryMode != nextMode;
            AudienceCount = audienceCount;
            DeliveryMode = nextMode;
            LastEvaluatedAtUtc = observed;
            UpdatedAtUtc = observed;
            if (changed)
                Version++;
            return changed;
        }

        private static void ValidateThresholds(int enterPullThreshold, int exitPullThreshold)
        {
            if (enterPullThreshold <= 0
                || exitPullThreshold < 0
                || exitPullThreshold >= enterPullThreshold)
            {
                throw new ArgumentException("Pull hysteresis requires 0 <= exit < enter.");
            }
        }
    }

    public sealed class FeedRevocationWorkItem
    {
        public long Id { get; private set; }
        public string SourceEventId { get; private set; } = string.Empty;
        public string RevocationKind { get; private set; } = string.Empty;
        public long? ActivityId { get; private set; }
        public FeedActivity? Activity { get; private set; }
        public int? OwnerUserId { get; private set; }
        public int? ViewerUserId { get; private set; }
        public string? AudiencePolicy { get; private set; }
        public long NextEntryId { get; private set; }
        public int Priority { get; private set; }
        public string State { get; private set; } = string.Empty;
        public int AttemptCount { get; private set; }
        public string? LeaseOwner { get; private set; }
        public Guid? LeaseToken { get; private set; }
        public DateTime? LeaseExpiresAtUtc { get; private set; }
        public string? LastErrorCode { get; private set; }
        public DateTime? CompletedAtUtc { get; private set; }
        public DateTime SourceOccurredAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedRevocationWorkItem() { }

        public static FeedRevocationWorkItem ForActivity(
            string sourceEventId,
            FeedActivity activity,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc) =>
            Create(
                sourceEventId,
                FeedRevocationKinds.Activity,
                activity ?? throw new ArgumentNullException(nameof(activity)),
                activity.ActorUserId,
                null,
                null,
                priority: 100,
                sourceOccurredAtUtc,
                observedAtUtc);

        public static FeedRevocationWorkItem ForRelationship(
            string sourceEventId,
            int ownerUserId,
            int viewerUserId,
            string audiencePolicy,
            bool isBlock,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc) =>
            Create(
                sourceEventId,
                isBlock ? FeedRevocationKinds.Block : FeedRevocationKinds.Relationship,
                null,
                ownerUserId,
                viewerUserId,
                audiencePolicy,
                priority: isBlock ? 200 : 150,
                sourceOccurredAtUtc,
                observedAtUtc);

        public static FeedRevocationWorkItem ForFriendsOfFriendsRelationship(
            string sourceEventId,
            int ownerUserId,
            int viewerUserId,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc) =>
            Create(
                sourceEventId,
                FeedRevocationKinds.FriendsOfFriends,
                null,
                ownerUserId,
                viewerUserId,
                FeedAudiencePolicy.FriendsOfFriends,
                priority: 175,
                sourceOccurredAtUtc,
                observedAtUtc);

        private static FeedRevocationWorkItem Create(
            string sourceEventId,
            string kind,
            FeedActivity? activity,
            int? ownerUserId,
            int? viewerUserId,
            string? audiencePolicy,
            int priority,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            if (ownerUserId <= 0 || viewerUserId <= 0)
            {
                if (activity is null)
                    throw new ArgumentOutOfRangeException(nameof(ownerUserId));
            }
            var sourceOccurred = FeedGuard.Utc(
                sourceOccurredAtUtc,
                nameof(sourceOccurredAtUtc));
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            return new FeedRevocationWorkItem
            {
                SourceEventId = FeedGuard.Required(
                    sourceEventId,
                    FeedSchemaLimits.SourceEventIdLength,
                    nameof(sourceEventId)),
                RevocationKind = FeedGuard.Normalized(
                    kind,
                    FeedSchemaLimits.StateLength,
                    nameof(kind)),
                Activity = activity,
                ActivityId = activity?.Id,
                OwnerUserId = ownerUserId,
                ViewerUserId = viewerUserId,
                AudiencePolicy = string.IsNullOrWhiteSpace(audiencePolicy)
                    ? null
                    : FeedGuard.Normalized(
                        audiencePolicy,
                        FeedSchemaLimits.AudiencePolicyLength,
                        nameof(audiencePolicy)),
                Priority = priority,
                State = FeedWorkStates.Pending,
                SourceOccurredAtUtc = sourceOccurred,
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                Version = 1,
            };
        }

        public Guid Lease(string owner, DateTime observedAtUtc, TimeSpan duration)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(5))
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (State == FeedWorkStates.Completed)
                throw new InvalidOperationException("Completed revocation work cannot be leased.");
            if (State == FeedWorkStates.Processing && LeaseExpiresAtUtc > observed)
                throw new InvalidOperationException("Revocation work already has an active lease.");
            LeaseOwner = FeedGuard.Required(
                owner,
                FeedSchemaLimits.LeaseOwnerLength,
                nameof(owner));
            LeaseToken = Guid.NewGuid();
            LeaseExpiresAtUtc = observed.Add(duration);
            State = FeedWorkStates.Processing;
            AttemptCount++;
            UpdatedAtUtc = observed;
            Version++;
            return LeaseToken.Value;
        }

        public void Advance(
            Guid leaseToken,
            long nextEntryId,
            bool completed,
            DateTime observedAtUtc)
        {
            RequireLease(leaseToken, observedAtUtc);
            if (nextEntryId < NextEntryId)
                throw new InvalidOperationException("Revocation progress cannot move backwards.");
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            NextEntryId = nextEntryId;
            State = completed ? FeedWorkStates.Completed : FeedWorkStates.Pending;
            CompletedAtUtc = completed ? observed : null;
            LeaseOwner = null;
            LeaseToken = null;
            LeaseExpiresAtUtc = null;
            LastErrorCode = null;
            UpdatedAtUtc = observed;
            Version++;
        }

        public void Fail(
            Guid leaseToken,
            string errorCode,
            bool terminal,
            DateTime observedAtUtc)
        {
            RequireLease(leaseToken, observedAtUtc);
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            LastErrorCode = FeedGuard.Normalized(
                errorCode,
                FeedSchemaLimits.ErrorCodeLength,
                nameof(errorCode));
            State = terminal ? FeedWorkStates.Failed : FeedWorkStates.Pending;
            CompletedAtUtc = terminal ? observed : null;
            LeaseOwner = null;
            LeaseToken = null;
            LeaseExpiresAtUtc = null;
            UpdatedAtUtc = observed;
            Version++;
        }

        private void RequireLease(Guid token, DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            if (token == Guid.Empty
                || LeaseToken != token
                || State != FeedWorkStates.Processing
                || LeaseExpiresAtUtc <= observed)
            {
                throw new InvalidOperationException("The revocation lease is missing, stale, or expired.");
            }
        }
    }

    public sealed class FeedMaintenanceCheckpoint
    {
        public string JobName { get; private set; } = string.Empty;
        public long Cursor { get; private set; }
        public DateTime LastCompletedAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedMaintenanceCheckpoint() { }

        public static FeedMaintenanceCheckpoint Create(string jobName, DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            return new FeedMaintenanceCheckpoint
            {
                JobName = FeedGuard.Normalized(
                    jobName,
                    FeedSchemaLimits.StateLength,
                    nameof(jobName)),
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                LastCompletedAtUtc = observed,
                Version = 1,
            };
        }

        public void Advance(long cursor, DateTime observedAtUtc)
        {
            if (cursor < Cursor)
                throw new InvalidOperationException("Maintenance progress cannot move backwards.");
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Cursor = cursor;
            LastCompletedAtUtc = observed;
            UpdatedAtUtc = observed;
            Version++;
        }

        public void Restart(DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Cursor = 0;
            LastCompletedAtUtc = observed;
            UpdatedAtUtc = observed;
            Version++;
        }
    }

    public sealed class FeedActivityEventQuarantine
    {
        public long Id { get; private set; }
        public Guid EventId { get; private set; }
        public Guid StreamId { get; private set; }
        public long StreamVersion { get; private set; }
        public string TargetType { get; private set; } = string.Empty;
        public string TargetId { get; private set; } = string.Empty;
        public string ActivityKind { get; private set; } = string.Empty;
        public int ActorUserId { get; private set; }
        public string AudiencePolicy { get; private set; } = string.Empty;
        public string State { get; private set; } = string.Empty;
        public long RankKey { get; private set; }
        public DateTime PublishedAtUtc { get; private set; }
        public DateTime SourceOccurredAtUtc { get; private set; }
        public string ReasonCode { get; private set; } = string.Empty;
        public DateTime RecordedAtUtc { get; private set; }
        public DateTime? ResolvedAtUtc { get; private set; }

        private FeedActivityEventQuarantine() { }

        public static FeedActivityEventQuarantine Create(
            Guid eventId,
            Guid streamId,
            long streamVersion,
            string targetType,
            string targetId,
            string activityKind,
            int actorUserId,
            string audiencePolicy,
            string state,
            long rankKey,
            DateTime publishedAtUtc,
            DateTime sourceOccurredAtUtc,
            string reasonCode,
            DateTime recordedAtUtc)
        {
            if (eventId == Guid.Empty || streamId == Guid.Empty)
                throw new ArgumentException("Event and stream IDs are required.");
            FeedGuard.Positive(streamVersion, nameof(streamVersion));
            FeedGuard.Positive(actorUserId, nameof(actorUserId));
            return new FeedActivityEventQuarantine
            {
                EventId = eventId,
                StreamId = streamId,
                StreamVersion = streamVersion,
                TargetType = FeedGuard.Normalized(targetType, FeedSchemaLimits.TypeLength, nameof(targetType)),
                TargetId = FeedGuard.Required(targetId, FeedSchemaLimits.TargetIdLength, nameof(targetId)),
                ActivityKind = FeedGuard.Normalized(activityKind, FeedSchemaLimits.EventKindLength, nameof(activityKind)),
                ActorUserId = actorUserId,
                AudiencePolicy = FeedGuard.Normalized(audiencePolicy, FeedSchemaLimits.AudiencePolicyLength, nameof(audiencePolicy)),
                State = FeedActivityLifecycle.Normalize(state),
                RankKey = rankKey,
                PublishedAtUtc = FeedGuard.Utc(publishedAtUtc, nameof(publishedAtUtc)),
                SourceOccurredAtUtc = FeedGuard.Utc(sourceOccurredAtUtc, nameof(sourceOccurredAtUtc)),
                ReasonCode = FeedGuard.Normalized(reasonCode, FeedSchemaLimits.ErrorCodeLength, nameof(reasonCode)),
                RecordedAtUtc = FeedGuard.Utc(recordedAtUtc, nameof(recordedAtUtc)),
            };
        }

        public void Resolve(DateTime resolvedAtUtc) =>
            ResolvedAtUtc ??= FeedGuard.Utc(resolvedAtUtc, nameof(resolvedAtUtc));
    }
}
