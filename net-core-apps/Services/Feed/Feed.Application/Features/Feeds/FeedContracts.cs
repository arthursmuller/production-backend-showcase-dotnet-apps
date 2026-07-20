using Feed.Application.Dtos;
using MessageBrokerContracts.Feed;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Application.Features.Feeds
{
    public interface IFeedActivityProjectionWriter
    {
        Task ApplyCanonicalAsync(
            FeedActivityChanged message,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    public interface IFeedFanoutProcessor
    {
        Task<int> ProcessBatchAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    public interface IFeedRevocationProcessor
    {
        Task<int> ProcessBatchAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    public interface IFeedMaintenanceProcessor
    {
        Task<int> CompactAsync(DateTime observedAtUtc, CancellationToken cancellationToken);
        Task<int> ReplayAsync(DateTime observedAtUtc, CancellationToken cancellationToken);
    }

    public sealed record FeedItem(
        long EntryId,
        long ActivityId,
        string TargetType,
        string TargetId,
        string ActivityKind,
        int ActorUserId,
        string AudiencePolicy,
        string Summary,
        long RankKey,
        DateTime PublishedAtUtc,
        DateTime? SeenAtUtc,
        string MaterializationMode,
        EngagementSummaryDto? Engagement = null);

    public sealed record FeedPage(
        IReadOnlyList<FeedItem> Items,
        string? NextCursor,
        bool HasMore,
        bool ContainsPullItems,
        long FeedRevision);

    public sealed record FeedPageRequest(
        string? Cursor,
        int PageSize = 20,
        bool IncludeSeen = true);

    public sealed record MarkFeedActivitiesSeen(IReadOnlyCollection<long> ActivityIds);

    public interface IFeedReader
    {
        Task<FeedPage> ReadAsync(
            int viewerUserId,
            FeedPageRequest request,
            CancellationToken cancellationToken);

        Task<int> AcknowledgeAsync(
            int viewerUserId,
            IReadOnlyCollection<long> activityIds,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    public sealed class FeedOptions
    {
        public const string SectionName = "Feed";

        public bool EnableFirstPageCache { get; set; } = true;
        public int PullEnterAudienceCount { get; set; } = 100_000;
        public int PullExitAudienceCount { get; set; } = 80_000;
        public int MaximumPullPublishers { get; set; } = 64;
        public int MaximumCandidates { get; set; } = 512;
        public int MaximumCandidatesPerPullPublisher { get; set; } = 8;
        public int MaximumMaterializedRows { get; set; } = 2_048;
        public int MaximumProcessedRows { get; set; } = 8_192;
        public int ReadDeadlineMilliseconds { get; set; } = 1_500;
        public int MergeDeadlineMilliseconds { get; set; } = 100;
        public int MaximumSerializedResponseBytes { get; set; } = 262_144;
        public int MaximumPageSize { get; set; } = 50;
        public int FanoutBatchSize { get; set; } = 500;
        public int FanoutClaimBatchSize { get; set; } = 16;
        public int RevocationBatchSize { get; set; } = 500;
        public int LeaseSeconds { get; set; } = 30;
        public int MaximumAttempts { get; set; } = 20;
        public int WorkerIntervalMilliseconds { get; set; } = 500;
        public int MaintenanceIntervalSeconds { get; set; } = 300;
        public int EntryRetentionDays { get; set; } = 30;
        public int CompletedWorkRetentionDays { get; set; } = 7;
        public int FirstPageCacheSeconds { get; set; } = 15;
        public int FirstPageLocalCacheSeconds { get; set; } = 5;
        public int MaximumCursorLength { get; set; } = 512;
        public int MaximumCheckpointAgeSeconds { get; set; } = 86400;
    }

    public static class FeedOptionsValidation
    {
        public static bool IsValid(FeedOptions options) =>
            options is not null
            && options.PullEnterAudienceCount is >= 1_000 and <= 10_000_000
            && options.PullExitAudienceCount is >= 0
            && options.PullExitAudienceCount < options.PullEnterAudienceCount
            && options.MaximumPullPublishers is >= 1 and <= 64
            && options.MaximumPageSize is >= 1 and <= 50
            && options.MaximumCandidates >= options.MaximumPageSize
            && options.MaximumCandidates >= options.MaximumPullPublishers * 2
            && options.MaximumCandidates <= 512
            && options.MaximumCandidatesPerPullPublisher is >= 1 and <= 64
            && options.MaximumCandidatesPerPullPublisher <= options.MaximumCandidates
            && options.MaximumMaterializedRows >= options.MaximumCandidates
            && options.MaximumMaterializedRows <= 8_192
            && options.MaximumProcessedRows >= options.MaximumCandidates
            && options.MaximumProcessedRows <= 32_768
            && options.ReadDeadlineMilliseconds is >= 100 and <= 5_000
            && options.MergeDeadlineMilliseconds is >= 1 and <= 1_000
            && options.MergeDeadlineMilliseconds <= options.ReadDeadlineMilliseconds
            && options.MaximumSerializedResponseBytes is >= 4_096 and <= 1_048_576
            && options.FanoutBatchSize is >= 1 and <= 500
            && options.FanoutClaimBatchSize is >= 1 and <= 64
            && options.RevocationBatchSize is >= 1 and <= 500
            && options.LeaseSeconds is >= 5 and <= 300
            && options.MaximumAttempts is >= 1 and <= 100
            && options.WorkerIntervalMilliseconds is >= 50 and <= 60_000
            && options.MaintenanceIntervalSeconds is >= 10 and <= 86_400
            && options.EntryRetentionDays is >= 1 and <= 3_650
            && options.CompletedWorkRetentionDays is >= 1 and <= 365
            && options.FirstPageCacheSeconds is >= 1 and <= 300
            && options.FirstPageLocalCacheSeconds is >= 1 and <= 60
            && options.FirstPageLocalCacheSeconds <= options.FirstPageCacheSeconds
            && options.MaximumCursorLength is >= 128 and <= 2_048
            && options.MaximumCheckpointAgeSeconds is >= 60 and <= 604_800;
    }

    public readonly record struct FeedSortTuple(long RankKey, long PublishedUtcTicks, long ActivityId)
        : IComparable<FeedSortTuple>
    {
        public int CompareTo(FeedSortTuple other)
        {
            var rank = RankKey.CompareTo(other.RankKey);
            if (rank != 0)
                return rank;
            var published = PublishedUtcTicks.CompareTo(other.PublishedUtcTicks);
            return published != 0 ? published : ActivityId.CompareTo(other.ActivityId);
        }
    }

    public sealed record FeedCursor(
        long SnapshotUtcTicks,
        FeedSortTuple HighWatermark,
        FeedSortTuple Last);
}
