using System;

namespace Feed.Domain.AggregatesModel
{
    public sealed class FeedActivity
    {
        public long Id { get; private set; }
        public string TargetType { get; private set; } = string.Empty;
        public string TargetId { get; private set; } = string.Empty;
        public string ActivityKind { get; private set; } = string.Empty;
        public int ActorUserId { get; private set; }
        public string AudiencePolicy { get; private set; } = string.Empty;
        public Guid? SourceStreamId { get; private set; }
        public Guid? LastEventId { get; private set; }
        public long SourceVersion { get; private set; }
        public long RankKey { get; private set; }
        public string DeliveryMode { get; private set; } = string.Empty;
        public string Summary { get; private set; } = string.Empty;
        public DateTime PublishedAtUtc { get; private set; }
        public bool IsTombstoned { get; private set; }
        public DateTime? TombstonedAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedActivity() { }

        public static FeedActivity Create(
            string targetType,
            string targetId,
            string activityKind,
            int actorUserId,
            string audiencePolicy,
            DateTime publishedAtUtc,
            DateTime createdAtUtc,
            Guid? sourceStreamId = null,
            Guid? eventId = null,
            long sourceVersion = 1,
            long? rankKey = null,
            string deliveryMode = FeedDeliveryModes.Push,
            string summary = "")
        {
            FeedGuard.Positive(actorUserId, nameof(actorUserId));
            FeedGuard.Positive(sourceVersion, nameof(sourceVersion));
            var created = FeedGuard.Utc(createdAtUtc, nameof(createdAtUtc));
            var published = FeedGuard.Utc(publishedAtUtc, nameof(publishedAtUtc));
            return new FeedActivity
            {
                TargetType = FeedGuard.Normalized(targetType, FeedSchemaLimits.TypeLength, nameof(targetType)),
                TargetId = FeedGuard.Required(targetId, FeedSchemaLimits.TargetIdLength, nameof(targetId)),
                ActivityKind = FeedGuard.Normalized(activityKind, FeedSchemaLimits.EventKindLength, nameof(activityKind)),
                ActorUserId = actorUserId,
                AudiencePolicy = FeedGuard.Normalized(audiencePolicy, FeedSchemaLimits.AudiencePolicyLength, nameof(audiencePolicy)),
                SourceStreamId = sourceStreamId ?? Guid.NewGuid(),
                LastEventId = eventId ?? Guid.NewGuid(),
                SourceVersion = sourceVersion,
                RankKey = rankKey ?? published.Ticks,
                DeliveryMode = FeedDeliveryModes.Normalize(deliveryMode),
                Summary = FeedGuard.Optional(
                    summary,
                    FeedSchemaLimits.DescriptionLength,
                    nameof(summary)),
                PublishedAtUtc = published,
                CreatedAtUtc = created,
                UpdatedAtUtc = created,
                Version = 1,
            };
        }

        public void ApplyNext(
            Guid sourceStreamId,
            Guid eventId,
            long sourceVersion,
            string audiencePolicy,
            string state,
            DateTime occurredAtUtc)
        {
            if (sourceStreamId == Guid.Empty || eventId == Guid.Empty)
                throw new ArgumentException("Source stream and event IDs are required.");
            if (SourceStreamId.HasValue && SourceStreamId != sourceStreamId)
                throw new InvalidOperationException("The activity stream identity changed.");
            if (sourceVersion != SourceVersion + 1)
                throw new InvalidOperationException("Only the next activity stream version can be applied.");

            var normalizedState = FeedActivityLifecycle.Normalize(state);
            var occurred = FeedGuard.Utc(occurredAtUtc, nameof(occurredAtUtc));
            SourceStreamId = sourceStreamId;
            LastEventId = eventId;
            SourceVersion = sourceVersion;
            AudiencePolicy = FeedGuard.Normalized(
                audiencePolicy,
                FeedSchemaLimits.AudiencePolicyLength,
                nameof(audiencePolicy));
            IsTombstoned = normalizedState != FeedActivityLifecycle.Active;
            TombstonedAtUtc = IsTombstoned ? occurred : null;
            UpdatedAtUtc = occurred;
            Version++;
        }

        public void Tombstone(DateTime tombstonedAtUtc)
        {
            if (IsTombstoned)
                return;

            var timestamp = FeedGuard.Utc(tombstonedAtUtc, nameof(tombstonedAtUtc));
            IsTombstoned = true;
            TombstonedAtUtc = timestamp;
            UpdatedAtUtc = timestamp;
            Version++;
        }

    }

    public sealed class FeedEntry
    {
        private FeedActivity? _activity;

        public long Id { get; private set; }
        public int RecipientUserId { get; private set; }
        public long ActivityId { get; private set; }
        public FeedActivity Activity
        {
            get => _activity
                ?? throw new InvalidOperationException("The feed activity navigation is not loaded.");
            private set => _activity = value;
        }
        public string MaterializationMode { get; private set; } = string.Empty;
        public long RankKey { get; private set; }
        public DateTime PublishedAtUtc { get; private set; }
        public DateTime? SeenAtUtc { get; private set; }
        public DateTime? RevokedAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedEntry() { }

        public static FeedEntry Create(
            FeedActivity activity,
            int recipientUserId,
            string materializationMode,
            long rankKey,
            DateTime createdAtUtc)
        {
            if (activity is null)
                throw new ArgumentNullException(nameof(activity));
            FeedGuard.Positive(recipientUserId, nameof(recipientUserId));

            var created = FeedGuard.Utc(createdAtUtc, nameof(createdAtUtc));
            return new FeedEntry
            {
                Activity = activity,
                ActivityId = activity.Id,
                RecipientUserId = recipientUserId,
                MaterializationMode = FeedGuard.Normalized(
                    materializationMode,
                    FeedSchemaLimits.MaterializationModeLength,
                    nameof(materializationMode)),
                RankKey = rankKey,
                PublishedAtUtc = activity.PublishedAtUtc,
                CreatedAtUtc = created,
                UpdatedAtUtc = created,
                Version = 1,
            };
        }

        public void Acknowledge(DateTime seenAtUtc)
        {
            if (SeenAtUtc.HasValue)
                return;

            var timestamp = FeedGuard.Utc(seenAtUtc, nameof(seenAtUtc));
            SeenAtUtc = timestamp;
            UpdatedAtUtc = timestamp;
            Version++;
        }

        public void Revoke(DateTime revokedAtUtc)
        {
            if (RevokedAtUtc.HasValue)
                return;

            var timestamp = FeedGuard.Utc(revokedAtUtc, nameof(revokedAtUtc));
            RevokedAtUtc = timestamp;
            UpdatedAtUtc = timestamp;
            Version++;
        }

        public void Restore(DateTime restoredAtUtc)
        {
            if (!RevokedAtUtc.HasValue)
                return;

            var timestamp = FeedGuard.Utc(restoredAtUtc, nameof(restoredAtUtc));
            RevokedAtUtc = null;
            UpdatedAtUtc = timestamp;
            Version++;
        }
    }

    public sealed class FeedAudienceEdge
    {
        public long Id { get; private set; }
        public int OwnerUserId { get; private set; }
        public int ViewerUserId { get; private set; }
        public string RelationshipKind { get; private set; } = string.Empty;
        public string State { get; private set; } = string.Empty;
        public Guid? SourceStreamId { get; private set; }
        public Guid? LastEventId { get; private set; }
        public long SourceVersion { get; private set; }
        public DateTime? SourceOccurredAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedAudienceEdge() { }

        public static FeedAudienceEdge Create(
            int ownerUserId,
            int viewerUserId,
            string relationshipKind,
            string state,
            Guid sourceStreamId,
            Guid eventId,
            long sourceVersion,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            FeedGuard.Positive(ownerUserId, nameof(ownerUserId));
            FeedGuard.Positive(viewerUserId, nameof(viewerUserId));
            FeedGuard.Positive(sourceVersion, nameof(sourceVersion));
            if (sourceStreamId == Guid.Empty)
                throw new ArgumentException("A source stream ID is required.", nameof(sourceStreamId));
            if (eventId == Guid.Empty)
                throw new ArgumentException("An event ID is required.", nameof(eventId));
            if (ownerUserId == viewerUserId)
                throw new ArgumentException("Feed audience edges cannot reference the same user in both directions.");

            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            return new FeedAudienceEdge
            {
                OwnerUserId = ownerUserId,
                ViewerUserId = viewerUserId,
                RelationshipKind = FeedGuard.Normalized(
                    relationshipKind,
                    FeedSchemaLimits.RelationshipKindLength,
                    nameof(relationshipKind)),
                State = FeedGuard.Normalized(state, FeedSchemaLimits.StateLength, nameof(state)),
                SourceStreamId = sourceStreamId,
                LastEventId = eventId,
                SourceVersion = sourceVersion,
                SourceOccurredAtUtc = FeedGuard.Utc(
                    sourceOccurredAtUtc,
                    nameof(sourceOccurredAtUtc)),
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                Version = 1,
            };
        }

        public void ApplyNext(
            Guid sourceStreamId,
            Guid eventId,
            string state,
            long sourceVersion,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            if (SourceStreamId != sourceStreamId)
                throw new InvalidOperationException("The relationship stream identity changed.");
            if (sourceVersion != SourceVersion + 1)
                throw new InvalidOperationException("Only the next relationship version can be applied.");

            Apply(eventId, state, sourceVersion, sourceOccurredAtUtc, observedAtUtc);
        }

        public void Reconcile(
            Guid sourceStreamId,
            Guid lastEventId,
            string state,
            long sourceVersion,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            if (SourceStreamId.HasValue && SourceStreamId != sourceStreamId)
                throw new InvalidOperationException("The relationship stream identity changed.");
            if (sourceVersion < SourceVersion)
                return;
            if (lastEventId == Guid.Empty)
                throw new ArgumentException("A last event ID is required.", nameof(lastEventId));

            SourceStreamId = sourceStreamId;
            Apply(lastEventId, state, sourceVersion, sourceOccurredAtUtc, observedAtUtc);
        }

        private void Apply(
            Guid eventId,
            string state,
            long sourceVersion,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            State = FeedGuard.Normalized(state, FeedSchemaLimits.StateLength, nameof(state));
            LastEventId = eventId == Guid.Empty ? LastEventId : eventId;
            SourceVersion = sourceVersion;
            SourceOccurredAtUtc = FeedGuard.Utc(sourceOccurredAtUtc, nameof(sourceOccurredAtUtc));
            UpdatedAtUtc = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Version++;
        }
    }

    public sealed class FeedUserState
    {
        public int UserId { get; private set; }
        public long FeedRevision { get; private set; }
        public DateTime? LastAcknowledgedAtUtc { get; private set; }
        public string? CompactionState { get; private set; }
        public string? CompactionCursor { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedUserState() { }

        public static FeedUserState Create(int userId, DateTime createdAtUtc)
        {
            FeedGuard.Positive(userId, nameof(userId));
            var created = FeedGuard.Utc(createdAtUtc, nameof(createdAtUtc));
            return new FeedUserState
            {
                UserId = userId,
                FeedRevision = 0,
                CreatedAtUtc = created,
                UpdatedAtUtc = created,
                Version = 1,
            };
        }

        public void Acknowledge(DateTime acknowledgedAtUtc)
        {
            var timestamp = FeedGuard.Utc(acknowledgedAtUtc, nameof(acknowledgedAtUtc));
            if (LastAcknowledgedAtUtc.HasValue && LastAcknowledgedAtUtc.Value >= timestamp)
                return;

            LastAcknowledgedAtUtc = timestamp;
            UpdatedAtUtc = timestamp;
            Version++;
        }

        public void IncrementRevision(DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            FeedRevision++;
            UpdatedAtUtc = observed;
            Version++;
        }

        public void SetCompactionProgress(
            string state,
            string cursor,
            DateTime observedAtUtc)
        {
            CompactionState = FeedGuard.Normalized(
                state,
                FeedSchemaLimits.StateLength,
                nameof(state));
            CompactionCursor = FeedGuard.Optional(
                cursor,
                FeedSchemaLimits.CursorLength,
                nameof(cursor));
            UpdatedAtUtc = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Version++;
        }
    }

    public sealed class FanoutWorkItem
    {
        private FeedActivity? _activity;

        public long Id { get; private set; }
        public string SourceEventId { get; private set; } = string.Empty;
        public long ActivityId { get; private set; }
        public FeedActivity Activity
        {
            get => _activity
                ?? throw new InvalidOperationException("The fan-out activity navigation is not loaded.");
            private set => _activity = value;
        }
        public string State { get; private set; } = string.Empty;
        public string? NextCursor { get; private set; }
        public int AttemptCount { get; private set; }
        public string? LeaseOwner { get; private set; }
        public DateTime? LeaseExpiresAtUtc { get; private set; }
        public string? LastErrorCode { get; private set; }
        public int PublisherUserId { get; private set; }
        public string WorkKind { get; private set; } = string.Empty;
        public int? NextViewerUserId { get; private set; }
        public int? AudienceHighWatermarkUserId { get; private set; }
        public Guid? LeaseToken { get; private set; }
        public DateTime? CompletedAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FanoutWorkItem() { }

        public static FanoutWorkItem Create(
            string sourceEventId,
            FeedActivity activity,
            DateTime createdAtUtc,
            string workKind = FeedWorkKinds.Fanout)
        {
            if (activity is null)
                throw new ArgumentNullException(nameof(activity));

            var created = FeedGuard.Utc(createdAtUtc, nameof(createdAtUtc));
            return new FanoutWorkItem
            {
                SourceEventId = FeedGuard.Required(
                    sourceEventId,
                    FeedSchemaLimits.SourceEventIdLength,
                    nameof(sourceEventId)),
                Activity = activity,
                ActivityId = activity.Id,
                PublisherUserId = activity.ActorUserId,
                WorkKind = FeedWorkKinds.Normalize(workKind),
                State = "pending",
                CreatedAtUtc = created,
                UpdatedAtUtc = created,
                Version = 1,
            };
        }

        public Guid Lease(
            string leaseOwner,
            DateTime observedAtUtc,
            TimeSpan leaseDuration)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(5))
                throw new ArgumentOutOfRangeException(nameof(leaseDuration));
            if (State == FeedWorkStates.Completed)
                throw new InvalidOperationException("Completed fan-out work cannot be leased.");
            if (State == FeedWorkStates.Processing && LeaseExpiresAtUtc > observed)
                throw new InvalidOperationException("Fan-out work already has an active lease.");

            LeaseOwner = FeedGuard.Required(
                leaseOwner,
                FeedSchemaLimits.LeaseOwnerLength,
                nameof(leaseOwner));
            LeaseToken = Guid.NewGuid();
            LeaseExpiresAtUtc = observed.Add(leaseDuration);
            State = FeedWorkStates.Processing;
            AttemptCount++;
            UpdatedAtUtc = observed;
            Version++;
            return LeaseToken.Value;
        }

        public void InitializeAudienceSnapshot(
            Guid leaseToken,
            int highWatermarkUserId,
            DateTime observedAtUtc)
        {
            RequireLease(leaseToken, observedAtUtc);
            if (highWatermarkUserId < 0)
                throw new ArgumentOutOfRangeException(nameof(highWatermarkUserId));
            AudienceHighWatermarkUserId ??= highWatermarkUserId;
            UpdatedAtUtc = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Version++;
        }

        public void Advance(
            Guid leaseToken,
            int nextViewerUserId,
            bool completed,
            DateTime observedAtUtc)
        {
            RequireLease(leaseToken, observedAtUtc);
            if (nextViewerUserId < (NextViewerUserId ?? 0))
                throw new InvalidOperationException("Fan-out progress cannot move backwards.");
            if (AudienceHighWatermarkUserId.HasValue
                && nextViewerUserId > AudienceHighWatermarkUserId.Value)
            {
                throw new InvalidOperationException("Fan-out progress exceeded its audience snapshot.");
            }

            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            NextViewerUserId = nextViewerUserId;
            NextCursor = nextViewerUserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        public void DeferForProjection(
            Guid leaseToken,
            string errorCode,
            DateTime observedAtUtc)
        {
            RequireLease(leaseToken, observedAtUtc);
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            LastErrorCode = FeedGuard.Normalized(
                errorCode,
                FeedSchemaLimits.ErrorCodeLength,
                nameof(errorCode));
            State = FeedWorkStates.Pending;
            AttemptCount = Math.Max(0, AttemptCount - 1);
            LeaseOwner = null;
            LeaseToken = null;
            LeaseExpiresAtUtc = null;
            UpdatedAtUtc = observed;
            Version++;
        }

        private void RequireLease(Guid leaseToken, DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            if (leaseToken == Guid.Empty
                || LeaseToken != leaseToken
                || State != FeedWorkStates.Processing
                || LeaseExpiresAtUtc <= observed)
            {
                throw new InvalidOperationException("The fan-out lease is missing, stale, or expired.");
            }
        }
    }

    internal static class FeedGuard
    {
        internal static string Required(string value, int maximumLength, string parameterName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ArgumentException("A value is required.", parameterName);
            if (normalized.Length > maximumLength)
                throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);

            return normalized;
        }

        internal static string Normalized(string value, int maximumLength, string parameterName) =>
            Required(value, maximumLength, parameterName).ToLowerInvariant();

        internal static string Optional(string value, int maximumLength, string parameterName)
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length > maximumLength)
                throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
            return normalized;
        }

        internal static DateTime Utc(DateTime value, string parameterName)
        {
            if (value == default)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);

            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }

        internal static void Positive(long value, string parameterName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(parameterName, "The value must be greater than zero.");
        }
    }
}
