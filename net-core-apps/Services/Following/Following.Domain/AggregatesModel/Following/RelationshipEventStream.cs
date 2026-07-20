using System;

namespace Following.Domain.AggregatesModel
{
    /// <summary>
    /// Never-deleted authority ledger for one directional relationship stream. Its version is
    /// independent of the relationship row, so deleting and recreating a relationship cannot
    /// reset ordering.
    /// </summary>
    public sealed class RelationshipEventStream
    {
        public long Id { get; private set; }
        public Guid StreamId { get; private set; }
        public int OwnerUserId { get; private set; }
        public int ViewerUserId { get; private set; }
        public string RelationshipKind { get; private set; } = string.Empty;
        public string CurrentState { get; private set; } = string.Empty;
        public long CurrentVersion { get; private set; }
        public Guid LastEventId { get; private set; }
        public DateTime LastOccurredAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private RelationshipEventStream() { }

        public static RelationshipEventStream Create(
            Guid streamId,
            Guid eventId,
            int ownerUserId,
            int viewerUserId,
            string relationshipKind,
            string state,
            DateTime occurredAtUtc)
        {
            Validate(streamId, eventId, ownerUserId, viewerUserId);
            var occurredAt = NormalizeUtc(occurredAtUtc);
            return new RelationshipEventStream
            {
                StreamId = streamId,
                OwnerUserId = ownerUserId,
                ViewerUserId = viewerUserId,
                RelationshipKind = Normalize(relationshipKind, 32, nameof(relationshipKind)),
                CurrentState = Normalize(state, 24, nameof(state)),
                CurrentVersion = 1,
                LastEventId = eventId,
                LastOccurredAtUtc = occurredAt,
                CreatedAtUtc = occurredAt,
                UpdatedAtUtc = occurredAt,
                Version = 1,
            };
        }

        public long Advance(Guid eventId, string state, DateTime occurredAtUtc)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("A non-empty event ID is required.", nameof(eventId));

            CurrentState = Normalize(state, 24, nameof(state));
            CurrentVersion = checked(CurrentVersion + 1);
            LastEventId = eventId;
            LastOccurredAtUtc = NormalizeUtc(occurredAtUtc);
            UpdatedAtUtc = LastOccurredAtUtc;
            Version = checked(Version + 1);
            return CurrentVersion;
        }

        private static void Validate(
            Guid streamId,
            Guid eventId,
            int ownerUserId,
            int viewerUserId)
        {
            if (streamId == Guid.Empty)
                throw new ArgumentException("A non-empty stream ID is required.", nameof(streamId));
            if (eventId == Guid.Empty)
                throw new ArgumentException("A non-empty event ID is required.", nameof(eventId));
            if (ownerUserId <= 0)
                throw new ArgumentOutOfRangeException(nameof(ownerUserId));
            if (viewerUserId <= 0)
                throw new ArgumentOutOfRangeException(nameof(viewerUserId));
            if (ownerUserId == viewerUserId)
                throw new ArgumentException("Relationship streams must be directional.");
        }

        private static string Normalize(string value, int maximumLength, string parameterName)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
                throw new ArgumentException("A bounded relationship value is required.", parameterName);
            return normalized;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default)
                throw new ArgumentException("A relationship timestamp is required.", nameof(value));
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }
    }
}
