using System;
using System.Linq;

namespace Feed.Domain.AggregatesModel
{
    public static class FeedEngagementResourceTypes
    {
        public const string Comment = "comment";
        public const string Post = "post";
        public const int MaximumIdLength = 128;

        public static string Normalize(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is Comment or Post
                ? normalized
                : throw new ArgumentException(
                    "Engagement resource type must be 'comment' or 'post'.",
                    nameof(value));
        }

        public static string NormalizeId(string value)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ArgumentException("An engagement resource ID is required.", nameof(value));
            if (normalized.Length > MaximumIdLength)
            {
                throw new ArgumentException(
                    $"Engagement resource ID cannot exceed {MaximumIdLength} characters.",
                    nameof(value));
            }
            if (normalized.Any(char.IsControl))
                throw new ArgumentException("Engagement resource ID cannot contain control characters.", nameof(value));
            return normalized;
        }
    }

    public static class FeedEngagementProjectionStates
    {
        public const string Uninitialized = "uninitialized";
        public const string Healthy = "healthy";
        public const string Gap = "gap";
        public const string Quarantined = "quarantined";
    }

    public enum FeedEngagementVersionDisposition
    {
        Next,
        StaleOrDuplicate,
        Gap,
        Quarantined,
    }

    /// <summary>
    /// Feed-owned public engagement read model. A non-healthy state is deliberately
    /// unreadable so an ordering gap can never make stale counters look authoritative.
    /// </summary>
    public sealed class FeedEngagementSummary
    {
        public string ResourceType { get; private set; } = string.Empty;
        public string ResourceId { get; private set; } = string.Empty;
        public long LikeCount { get; private set; }
        public long DislikeCount { get; private set; }
        public long RatingCount { get; private set; }
        public long RatingTotal { get; private set; }
        public long ViewCount { get; private set; }
        public long CommentCount { get; private set; }
        public long ShareCount { get; private set; }
        public long LastAppliedVersion { get; private set; }
        public long HighestObservedVersion { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public long? GapFromVersion { get; private set; }
        public long? GapToVersion { get; private set; }
        public Guid? LastEventId { get; private set; }
        public DateTime? LastSourceOccurredAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public long Version { get; private set; }

        private FeedEngagementSummary() { }

        public bool IsPubliclyReadable =>
            Status == FeedEngagementProjectionStates.Healthy
            && LastAppliedVersion > 0
            && LastAppliedVersion == HighestObservedVersion;

        public static FeedEngagementSummary CreateCheckpoint(
            string resourceType,
            string resourceId,
            DateTime observedAtUtc)
        {
            var observed = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            return new FeedEngagementSummary
            {
                ResourceType = FeedEngagementResourceTypes.Normalize(resourceType),
                ResourceId = FeedEngagementResourceTypes.NormalizeId(resourceId),
                Status = FeedEngagementProjectionStates.Uninitialized,
                CreatedAtUtc = observed,
                UpdatedAtUtc = observed,
                Version = 1,
            };
        }

        public FeedEngagementVersionDisposition Classify(long incomingVersion)
        {
            FeedGuard.Positive(incomingVersion, nameof(incomingVersion));
            if (Status == FeedEngagementProjectionStates.Quarantined)
                return FeedEngagementVersionDisposition.Quarantined;
            if (incomingVersion <= LastAppliedVersion)
                return FeedEngagementVersionDisposition.StaleOrDuplicate;
            // EngagementSummaryChanged is an absolute value but it is not a declared baseline
            // snapshot. In particular, first observing version N cannot prove that versions 1 through N-1
            // were processed, so it must follow the same retained, fail-closed gap path.
            return incomingVersion == LastAppliedVersion + 1
                ? FeedEngagementVersionDisposition.Next
                : FeedEngagementVersionDisposition.Gap;
        }

        public bool Matches(
            Guid eventId,
            long likeCount,
            long dislikeCount,
            long ratingCount,
            long ratingTotal,
            long viewCount,
            long commentCount,
            long shareCount,
            long summaryVersion,
            DateTime sourceOccurredAtUtc) =>
            LastEventId == eventId
            && LastAppliedVersion == summaryVersion
            && LikeCount == likeCount
            && DislikeCount == dislikeCount
            && RatingCount == ratingCount
            && RatingTotal == ratingTotal
            && ViewCount == viewCount
            && CommentCount == commentCount
            && ShareCount == shareCount
            && LastSourceOccurredAtUtc == FeedGuard.Utc(sourceOccurredAtUtc, nameof(sourceOccurredAtUtc));

        public void ApplyNext(
            Guid eventId,
            long likeCount,
            long dislikeCount,
            long ratingCount,
            long ratingTotal,
            long viewCount,
            long commentCount,
            long shareCount,
            long summaryVersion,
            DateTime sourceOccurredAtUtc,
            DateTime observedAtUtc)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("An engagement event ID is required.", nameof(eventId));
            if (summaryVersion != LastAppliedVersion + 1)
                throw new InvalidOperationException("Only the next engagement summary version can be applied.");
            ValidateCounters(
                likeCount,
                dislikeCount,
                ratingCount,
                ratingTotal,
                viewCount,
                commentCount,
                shareCount);

            LikeCount = likeCount;
            DislikeCount = dislikeCount;
            RatingCount = ratingCount;
            RatingTotal = ratingTotal;
            ViewCount = viewCount;
            CommentCount = commentCount;
            ShareCount = shareCount;
            LastAppliedVersion = summaryVersion;
            HighestObservedVersion = Math.Max(HighestObservedVersion, summaryVersion);
            Status = FeedEngagementProjectionStates.Healthy;
            GapFromVersion = null;
            GapToVersion = null;
            LastEventId = eventId;
            LastSourceOccurredAtUtc = FeedGuard.Utc(
                sourceOccurredAtUtc,
                nameof(sourceOccurredAtUtc));
            UpdatedAtUtc = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Version++;
        }

        public void MarkGap(long receivedVersion, DateTime observedAtUtc)
        {
            if (receivedVersion <= LastAppliedVersion + 1)
                throw new ArgumentOutOfRangeException(nameof(receivedVersion));
            HighestObservedVersion = Math.Max(HighestObservedVersion, receivedVersion);
            Status = FeedEngagementProjectionStates.Gap;
            GapFromVersion = LastAppliedVersion + 1;
            GapToVersion = Math.Max(GapToVersion ?? 0, receivedVersion - 1);
            UpdatedAtUtc = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Version++;
        }

        public void MarkQuarantined(long receivedVersion, DateTime observedAtUtc)
        {
            FeedGuard.Positive(receivedVersion, nameof(receivedVersion));
            HighestObservedVersion = Math.Max(HighestObservedVersion, receivedVersion);
            Status = FeedEngagementProjectionStates.Quarantined;
            UpdatedAtUtc = FeedGuard.Utc(observedAtUtc, nameof(observedAtUtc));
            Version++;
        }

        public static void ValidateCounters(
            long likeCount,
            long dislikeCount,
            long ratingCount,
            long ratingTotal,
            long viewCount,
            long commentCount,
            long shareCount)
        {
            if (likeCount < 0
                || dislikeCount < 0
                || ratingCount < 0
                || ratingTotal < 0
                || viewCount < 0
                || commentCount < 0
                || shareCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(likeCount),
                    "Engagement counters cannot be negative.");
            }

            var belowMinimum = ratingCount == 0 ? ratingTotal != 0 : ratingTotal < ratingCount;
            var exceedsMaximum = ratingCount <= long.MaxValue / 6
                && ratingTotal > ratingCount * 6;
            if (belowMinimum || exceedsMaximum)
            {
                throw new ArgumentException(
                    "Rating total must match a one-to-six rating scale.",
                    nameof(ratingTotal));
            }
        }
    }

    /// <summary>
    /// Durable retention for an out-of-order or conflicting absolute summary. Gap records are
    /// resolved, rather than deleted, so recovery remains auditable.
    /// </summary>
    public sealed class FeedEngagementSummaryEventQuarantine
    {
        public long Id { get; private set; }
        public Guid EventId { get; private set; }
        public string ResourceType { get; private set; } = string.Empty;
        public string ResourceId { get; private set; } = string.Empty;
        public long LikeCount { get; private set; }
        public long DislikeCount { get; private set; }
        public long RatingCount { get; private set; }
        public long RatingTotal { get; private set; }
        public long ViewCount { get; private set; }
        public long CommentCount { get; private set; }
        public long ShareCount { get; private set; }
        public long SummaryVersion { get; private set; }
        public long ExpectedVersion { get; private set; }
        public string ReasonCode { get; private set; } = string.Empty;
        public DateTime SourceOccurredAtUtc { get; private set; }
        public DateTime RecordedAtUtc { get; private set; }
        public DateTime? ResolvedAtUtc { get; private set; }
        public string? Resolution { get; private set; }

        private FeedEngagementSummaryEventQuarantine() { }

        public static FeedEngagementSummaryEventQuarantine Create(
            Guid eventId,
            string resourceType,
            string resourceId,
            long likeCount,
            long dislikeCount,
            long ratingCount,
            long ratingTotal,
            long viewCount,
            long commentCount,
            long shareCount,
            long summaryVersion,
            long expectedVersion,
            string reasonCode,
            DateTime sourceOccurredAtUtc,
            DateTime recordedAtUtc)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("An engagement event ID is required.", nameof(eventId));
            FeedGuard.Positive(summaryVersion, nameof(summaryVersion));
            FeedGuard.Positive(expectedVersion, nameof(expectedVersion));
            FeedEngagementSummary.ValidateCounters(
                likeCount,
                dislikeCount,
                ratingCount,
                ratingTotal,
                viewCount,
                commentCount,
                shareCount);
            return new FeedEngagementSummaryEventQuarantine
            {
                EventId = eventId,
                ResourceType = FeedEngagementResourceTypes.Normalize(resourceType),
                ResourceId = FeedEngagementResourceTypes.NormalizeId(resourceId),
                LikeCount = likeCount,
                DislikeCount = dislikeCount,
                RatingCount = ratingCount,
                RatingTotal = ratingTotal,
                ViewCount = viewCount,
                CommentCount = commentCount,
                ShareCount = shareCount,
                SummaryVersion = summaryVersion,
                ExpectedVersion = expectedVersion,
                ReasonCode = FeedGuard.Normalized(reasonCode, FeedSchemaLimits.ErrorCodeLength, nameof(reasonCode)),
                SourceOccurredAtUtc = FeedGuard.Utc(sourceOccurredAtUtc, nameof(sourceOccurredAtUtc)),
                RecordedAtUtc = FeedGuard.Utc(recordedAtUtc, nameof(recordedAtUtc)),
            };
        }

        public bool Matches(
            Guid eventId,
            long likeCount,
            long dislikeCount,
            long ratingCount,
            long ratingTotal,
            long viewCount,
            long commentCount,
            long shareCount,
            long summaryVersion,
            DateTime sourceOccurredAtUtc) =>
            EventId == eventId
            && LikeCount == likeCount
            && DislikeCount == dislikeCount
            && RatingCount == ratingCount
            && RatingTotal == ratingTotal
            && ViewCount == viewCount
            && CommentCount == commentCount
            && ShareCount == shareCount
            && SummaryVersion == summaryVersion
            && SourceOccurredAtUtc == FeedGuard.Utc(sourceOccurredAtUtc, nameof(sourceOccurredAtUtc));

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
