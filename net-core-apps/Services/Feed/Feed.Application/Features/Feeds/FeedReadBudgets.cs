using Feed.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Feed.Application.Features.Feeds
{
    public static class FeedReadBudgetReasons
    {
        public const string MaterializedRows = "materialized_rows";
        public const string ProcessedRows = "processed_rows";
        public const string ReadDeadline = "read_deadline";
        public const string MergeDeadline = "merge_deadline";
        public const string ResponseBytes = "response_bytes";
        public const string AudienceProjection = "audience_projection";
    }

    public sealed class FeedReadBudgetExceededException : FeedException
    {
        public FeedReadBudgetExceededException(string reason)
            : base("Feed capacity is temporarily unavailable. Retry with backoff.", 503)
        {
            if (reason is not (FeedReadBudgetReasons.MaterializedRows
                or FeedReadBudgetReasons.ProcessedRows
                or FeedReadBudgetReasons.ReadDeadline
                or FeedReadBudgetReasons.MergeDeadline
                or FeedReadBudgetReasons.ResponseBytes
                or FeedReadBudgetReasons.AudienceProjection))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }
            Reason = reason;
        }

        public string Reason { get; }
    }

    /// <summary>
    /// Per-request deterministic work accounting. A breach rejects the complete response; callers
    /// must never turn it into a partial page or advance a continuation boundary.
    /// </summary>
    public sealed class FeedReadWorkBudget
    {
        private static readonly JsonSerializerOptions ResponseJsonOptions =
            new(JsonSerializerDefaults.Web);
        private readonly FeedOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly long _startedAt;
        private TimeSpan _mergeElapsed;
        private int _materializedRows;
        private int _processedRows;

        public FeedReadWorkBudget(FeedOptions options, TimeProvider timeProvider)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _startedAt = _timeProvider.GetTimestamp();
        }

        public int RemainingMaterializedRows =>
            Math.Max(0, _options.MaximumMaterializedRows - _materializedRows);

        public void EnsureMaterializedCapacity(int maximumRows)
        {
            if (maximumRows < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumRows));
            if (maximumRows > RemainingMaterializedRows)
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.MaterializedRows);
            CheckReadDeadline();
        }

        public void Materialized(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            _materializedRows = checked(_materializedRows + count);
            if (_materializedRows > _options.MaximumMaterializedRows)
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.MaterializedRows);
            CheckReadDeadline();
        }

        public void Processed(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            _processedRows = checked(_processedRows + count);
            if (_processedRows > _options.MaximumProcessedRows)
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.ProcessedRows);
            CheckReadDeadline();
        }

        public IDisposable MeasureMerge() => new MergeMeasurement(this);

        public void CheckReadDeadline()
        {
            if (_timeProvider.GetElapsedTime(_startedAt)
                > TimeSpan.FromMilliseconds(_options.ReadDeadlineMilliseconds))
            {
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.ReadDeadline);
            }
        }

        public void EnsureResponseFits(FeedPage page)
        {
            ArgumentNullException.ThrowIfNull(page);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(page, ResponseJsonOptions).Length;
            if (bytes > _options.MaximumSerializedResponseBytes)
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.ResponseBytes);
            CheckReadDeadline();
        }

        private void FinishMerge(long startedAt)
        {
            _mergeElapsed += _timeProvider.GetElapsedTime(startedAt);
            if (_mergeElapsed > TimeSpan.FromMilliseconds(_options.MergeDeadlineMilliseconds))
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.MergeDeadline);
            CheckReadDeadline();
        }

        private sealed class MergeMeasurement(FeedReadWorkBudget owner) : IDisposable
        {
            private readonly long _startedAt = owner._timeProvider.GetTimestamp();
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                owner.FinishMerge(_startedAt);
            }
        }
    }

    public static class FeedReadPolicy
    {
        public static bool ShouldUseFirstPageCache(
            FeedOptions options,
            bool hasCursor,
            int pullPublisherCount) =>
            options is not null
            && options.EnableFirstPageCache
            && !hasCursor
            && pullPublisherCount == 0;

        public static int PerPublisherCandidateLimit(
            int totalPullLimit,
            int publisherCount,
            int hardPerPublisherLimit)
        {
            if (totalPullLimit <= 0
                || publisherCount <= 0
                || publisherCount > totalPullLimit
                || hardPerPublisherLimit <= 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            return Math.Min(hardPerPublisherLimit, Math.Max(1, totalPullLimit / publisherCount));
        }

        public static FeedFairSelection<T> SelectPerPublisher<T>(
            IReadOnlyList<T> globallySortedRows,
            Func<T, int> publisherSelector,
            int perPublisherLimit,
            int totalLimit)
        {
            ArgumentNullException.ThrowIfNull(globallySortedRows);
            ArgumentNullException.ThrowIfNull(publisherSelector);
            if (perPublisherLimit <= 0 || totalLimit <= 0)
                throw new ArgumentOutOfRangeException();

            var counts = new Dictionary<int, int>();
            var selected = new List<T>(Math.Min(totalLimit, globallySortedRows.Count));
            var excluded = new List<T>();
            foreach (var row in globallySortedRows)
            {
                var publisher = publisherSelector(row);
                if (publisher <= 0)
                    throw new ArgumentOutOfRangeException(nameof(publisherSelector));
                var count = counts.GetValueOrDefault(publisher) + 1;
                counts[publisher] = count;
                if (count > perPublisherLimit || selected.Count >= totalLimit)
                {
                    excluded.Add(row);
                    continue;
                }
                selected.Add(row);
            }
            return new FeedFairSelection<T>(selected, excluded);
        }

        public static bool IsNewerThanExcludedFrontier(
            FeedSortTuple candidate,
            FeedSortTuple excludedFrontier) =>
            candidate.CompareTo(excludedFrontier) > 0;
    }

    public sealed record FeedFairSelection<T>(
        IReadOnlyList<T> Items,
        IReadOnlyList<T> ExcludedItems)
    {
        public bool Truncated => ExcludedItems.Count > 0;
    }
}
