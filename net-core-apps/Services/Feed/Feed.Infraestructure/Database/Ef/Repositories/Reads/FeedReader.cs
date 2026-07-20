using Feed.Application.Dtos;
using Feed.Application.Features.Feeds;
using Feed.Application.Features.Privacy;
using Feed.Domain.AggregatesModel;
using Feed.Domain.AggregatesModel.Moderation;
using Inspacy.Caching;
using MessageBrokerContracts.Feed;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class FeedReader(
        FeedContext context,
        IFeedUserErasureAuthority erasureAuthority,
        ISharedCache cache,
        ICacheKeyFactory cacheKeys,
        IOptions<FeedOptions> options,
        IOptions<AudienceProjectionOptions> audienceOptions,
        IOptions<FeedCursorOptions> cursorOptions,
        FeedTelemetry telemetry,
        TimeProvider timeProvider) : IFeedReader
    {
        private readonly FeedOptions _options = options.Value;
        private readonly AudienceProjectionOptions _audienceOptions = audienceOptions.Value;
        private readonly FeedCursorOptions _cursorOptions = cursorOptions.Value;

        public async Task<FeedPage> ReadAsync(
            int viewerUserId,
            FeedPageRequest request,
            CancellationToken cancellationToken)
        {
            if (viewerUserId <= 0)
                throw new ArgumentOutOfRangeException(nameof(viewerUserId));
            ArgumentNullException.ThrowIfNull(request);
            if (request.PageSize is < 1 || request.PageSize > _options.MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(request.PageSize));
            cancellationToken.ThrowIfCancellationRequested();

            return await ExecuteReadPipelineAsync(
                viewerUserId,
                TimeSpan.FromMilliseconds(_options.ReadDeadlineMilliseconds),
                erasureAuthority,
                token => ReadCoreAsync(viewerUserId, request, token),
                telemetry,
                cancellationToken);
        }

        internal static async Task<FeedPage> ExecuteReadPipelineAsync(
            int viewerUserId,
            TimeSpan readDeadline,
            IFeedUserErasureAuthority authority,
            Func<CancellationToken, Task<FeedPage>> readCore,
            FeedTelemetry telemetry,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(authority);
            ArgumentNullException.ThrowIfNull(readCore);
            ArgumentNullException.ThrowIfNull(telemetry);
            if (readDeadline <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(readDeadline));

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(readDeadline);
            try
            {
                if (await authority.IsErasedAsync(viewerUserId, deadline.Token))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    return new FeedPage([], null, false, false, 0);
                }
                deadline.Token.ThrowIfCancellationRequested();

                var page = await readCore(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                var subjects = page.Items.Select(item => item.ActorUserId)
                    .Append(viewerUserId)
                    .Distinct()
                    .ToArray();
                var erased = await authority.GetErasedAsync(subjects, deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (erased.Contains(viewerUserId))
                    return new FeedPage([], null, false, false, 0);
                if (erased.Count == 0)
                    return page;
                var visible = page.Items
                    .Where(item => !erased.Contains(item.ActorUserId))
                    .ToArray();
                return page with
                {
                    Items = visible,
                    ContainsPullItems = visible.Any(item =>
                        item.MaterializationMode == FeedDeliveryModes.Pull),
                };
            }
            catch (FeedReadBudgetExceededException exception)
            {
                telemetry.RecordBudgetRejected(exception.Reason);
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                && deadline.IsCancellationRequested)
            {
                telemetry.RecordBudgetRejected(FeedReadBudgetReasons.ReadDeadline);
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.ReadDeadline);
            }
        }

        private async Task<FeedPage> ReadCoreAsync(
            int viewerUserId,
            FeedPageRequest request,
            CancellationToken cancellationToken)
        {
            var budget = new FeedReadWorkBudget(_options, timeProvider);
            var now = timeProvider.GetUtcNow();
            var revision = await context.FeedUserStates
                .AsNoTracking()
                .Where(state => state.UserId == viewerUserId)
                .Select(state => (long?)state.FeedRevision)
                .SingleOrDefaultAsync(cancellationToken) ?? 0;
            cancellationToken.ThrowIfCancellationRequested();
            budget.Materialized(1);
            var audienceBootstrapRevision = await context.FeedAudienceBootstrapStates
                .AsNoTracking()
                .Where(state => state.Id == FeedAudienceBootstrapState.SingletonId)
                .Select(state => (long?)state.Version)
                .SingleOrDefaultAsync(cancellationToken) ?? 0;
            budget.Materialized(1);

            FeedCursor? cursor = null;
            if (!string.IsNullOrWhiteSpace(request.Cursor))
            {
                cursor = FeedCursorCodec.Decode(
                    request.Cursor,
                    viewerUserId,
                    revision,
                    request.PageSize,
                    request.IncludeSeen,
                    FeedCursorSurfaces.Items,
                    _cursorOptions,
                    now,
                    _options.MaximumCursorLength);
            }
            var pullPublishers = await LoadPullPublishersAsync(
                viewerUserId,
                now.UtcDateTime,
                budget,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            FeedPage page;
            if (FeedReadPolicy.ShouldUseFirstPageCache(
                _options,
                cursor is not null,
                pullPublishers.Count))
            {
                var key = cacheKeys.Create(
                    "feed-first-page",
                    $"{viewerUserId}:{request.PageSize}:{request.IncludeSeen}:{audienceBootstrapRevision}",
                    entityVersion: revision);
                page = await cache.GetOrCreateAsync(
                    key,
                    token => new ValueTask<FeedPage>(LoadPageAsync(
                        viewerUserId,
                        request,
                        null,
                        revision,
                        pullPublishers,
                        budget,
                        token)),
                    new CacheEntryPolicy(
                        TimeSpan.FromSeconds(_options.FirstPageCacheSeconds),
                        TimeSpan.FromSeconds(_options.FirstPageLocalCacheSeconds)),
                    cancellationToken);
            }
            else
            {
                page = await LoadPageAsync(
                    viewerUserId,
                    request,
                    cursor,
                    revision,
                    pullPublishers,
                    budget,
                    cancellationToken);
            }

            budget.Processed(page.Items.Count);

            // Engagement health is intentionally evaluated after the page cache. A newly
            // detected source gap must suppress cached counters immediately, while the cached
            // candidate order remains deterministic and unchanged.
            page = await ApplyModerationSuppressionAsync(page, budget, cancellationToken);
            var hydrated = await HydrateEngagementAsync(page, budget, cancellationToken);
            budget.EnsureResponseFits(hydrated);
            return hydrated;
        }

        public async Task<int> AcknowledgeAsync(
            int viewerUserId,
            IReadOnlyCollection<long> activityIds,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (viewerUserId <= 0)
                throw new ArgumentOutOfRangeException(nameof(viewerUserId));
            if (activityIds is null
                || activityIds.Count is < 1 or > 100
                || activityIds.Any(id => id <= 0))
            {
                throw new ArgumentException("Feed activity IDs must contain 1-100 positive values.");
            }
            var ids = activityIds.Distinct().ToArray();
            var observed = NormalizeUtc(observedAtUtc);
            await using var mutation = await erasureAuthority.RequireActiveUsersAsync(
                [viewerUserId],
                cancellationToken);
            var budget = new FeedReadWorkBudget(_options, timeProvider);
            var alreadyAcknowledged = await context.FeedEntries
                .AsNoTracking()
                .Where(entry => entry.RecipientUserId == viewerUserId
                    && ids.Contains(entry.ActivityId)
                    && entry.SeenAtUtc != null)
                .Select(entry => entry.ActivityId)
                .ToListAsync(cancellationToken);
            budget.Materialized(alreadyAcknowledged.Count);
            var pendingIds = ids.Except(alreadyAcknowledged).ToArray();
            if (pendingIds.Length == 0)
                return 0;
            var activities = await context.FeedActivities
                .AsNoTracking()
                .Where(activity => pendingIds.Contains(activity.Id)
                    && !activity.IsTombstoned
                    && !context.FeedUserErasures.Any(erasure =>
                        erasure.UserId == activity.ActorUserId))
                .ToListAsync(cancellationToken);
            var actorIds = activities.Select(activity => activity.ActorUserId).Distinct().ToArray();
            await using var actorMutation = actorIds.Length == 0
                ? null
                : await erasureAuthority.AcquireMutationAsync(actorIds, cancellationToken);
            if (actorMutation is not null && !actorMutation.Allowed)
            {
                activities = activities
                    .Where(activity => !actorMutation.ErasedUserIds.Contains(activity.ActorUserId))
                    .ToList();
            }
            var candidates = activities.Select(activity => new Candidate(
                0,
                activity.Id,
                activity.TargetType,
                activity.TargetId,
                activity.ActivityKind,
                activity.ActorUserId,
                activity.AudiencePolicy,
                activity.Summary,
                activity.RankKey,
                activity.PublishedAtUtc,
                null,
                activity.DeliveryMode)).ToArray();
            var authorized = await AuthorizeAsync(
                viewerUserId,
                candidates,
                observed,
                budget,
                cancellationToken);
            var existingActivityIds = await context.FeedEntries
                .Where(entry => entry.RecipientUserId == viewerUserId
                    && pendingIds.Contains(entry.ActivityId))
                .Select(entry => entry.ActivityId)
                .ToListAsync(cancellationToken);
            var pullPublishers = await LoadPullPublishersAsync(
                viewerUserId,
                observed,
                budget,
                cancellationToken);
            var allowedIds = authorized
                .Where(candidate => existingActivityIds.Contains(candidate.ActivityId)
                    || (candidate.MaterializationMode == FeedDeliveryModes.Pull
                        && pullPublishers.Contains(candidate.ActorUserId)))
                .Select(candidate => candidate.ActivityId)
                .Distinct()
                .ToArray();
            if (allowedIds.Length == 0)
                return 0;

            var parameters = new List<MySqlParameter>
            {
                new("@viewer", viewerUserId),
                new("@observed", observed),
            };
            var activityParameters = new List<string>(allowedIds.Length);
            foreach (var activityId in allowedIds)
            {
                var name = $"@activity{activityParameters.Count}";
                activityParameters.Add(name);
                parameters.Add(new MySqlParameter(name, activityId));
            }
            // Only generated parameter names are interpolated; all values remain provider
            // parameters. Keeping the SQL in a variable also makes that boundary explicit.
            var insertSql = $"""
                INSERT IGNORE INTO `FeedEntry`
                    (`RecipientUserId`, `ActivityId`, `MaterializationMode`, `RankKey`,
                     `PublishedAtUtc`, `SeenAtUtc`, `RevokedAtUtc`, `CreatedAtUtc`,
                     `UpdatedAtUtc`, `Version`)
                SELECT @viewer, activity.`Id`, 'pull', activity.`RankKey`,
                       activity.`PublishedAtUtc`, @observed, NULL, @observed, @observed, 1
                FROM `FeedActivity` AS activity
                WHERE activity.`Id` IN ({string.Join(",", activityParameters)})
                  AND activity.`DeliveryMode` = 'pull'
                  AND activity.`IsTombstoned` = 0
                """;
            var inserted = await context.Database.ExecuteSqlRawAsync(
                insertSql,
                parameters.Cast<object>().ToArray(),
                cancellationToken);
            var updated = await context.FeedEntries
                .Where(entry => entry.RecipientUserId == viewerUserId
                    && entry.SeenAtUtc == null
                    && allowedIds.Contains(entry.ActivityId))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(entry => entry.SeenAtUtc, observed)
                        .SetProperty(entry => entry.UpdatedAtUtc, observed)
                        .SetProperty(entry => entry.Version, entry => entry.Version + 1),
                    cancellationToken);
            var changed = inserted + updated;
            if (changed == 0)
                return 0;

            var state = await context.FeedUserStates.SingleOrDefaultAsync(
                item => item.UserId == viewerUserId,
                cancellationToken);
            if (state is null)
            {
                state = FeedUserState.Create(viewerUserId, observed);
                await context.FeedUserStates.AddAsync(state, cancellationToken);
            }
            state.Acknowledge(observed);
            state.IncrementRevision(observed);
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            telemetry.RecordBatch("acknowledge", "push", changed);
            return changed;
        }

        private async Task<FeedPage> LoadPageAsync(
            int viewerUserId,
            FeedPageRequest request,
            FeedCursor? cursor,
            long revision,
            IReadOnlyCollection<int> pullPublishers,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            var snapshotAtUtc = cursor is null
                ? timeProvider.GetUtcNow().UtcDateTime
                : new DateTime(cursor.SnapshotUtcTicks, DateTimeKind.Utc);
            var totalBudget = _options.MaximumCandidates;
            var pullBudget = pullPublishers.Count == 0 ? 0 : totalBudget / 2;
            var pushBudget = totalBudget - pullBudget;
            var push = await LoadPushCandidatesAsync(
                viewerUserId,
                request.IncludeSeen,
                snapshotAtUtc,
                cursor,
                pushBudget,
                budget,
                cancellationToken);
            var pull = pullBudget == 0
                ? new CandidateLoadResult([], false, null)
                : await LoadPullCandidatesAsync(
                    viewerUserId,
                    pullPublishers,
                    request.IncludeSeen,
                    snapshotAtUtc,
                    cursor,
                    pullBudget,
                    budget,
                    cancellationToken);
            Candidate[] candidates;
            using (budget.MeasureMerge())
            {
                budget.Processed(push.Items.Count + pull.Items.Count);
                var excludedFrontier = HighestFrontier(
                    push.ExcludedFrontier,
                    pull.ExcludedFrontier);
                candidates = push.Items
                    .Concat(pull.Items)
                    .GroupBy(candidate => candidate.ActivityId)
                    .Select(group => group
                        .OrderBy(candidate => candidate.MaterializationMode == FeedDeliveryModes.Push ? 0 : 1)
                        .First())
                    .OrderByDescending(candidate => candidate.RankKey)
                    .ThenByDescending(candidate => candidate.PublishedAtUtc)
                    .ThenByDescending(candidate => candidate.ActivityId)
                    .Where(candidate => !excludedFrontier.HasValue
                        || FeedReadPolicy.IsNewerThanExcludedFrontier(
                            ToTuple(candidate),
                            excludedFrontier.Value))
                    .Take(totalBudget)
                    .ToArray();
            }

            var authorized = await AuthorizeAsync(
                viewerUserId,
                candidates,
                snapshotAtUtc,
                budget,
                cancellationToken);
            Candidate[] ordered;
            Candidate[] pageCandidates;
            using (budget.MeasureMerge())
            {
                budget.Processed(authorized.Count);
                ordered = authorized
                    .OrderByDescending(candidate => candidate.RankKey)
                    .ThenByDescending(candidate => candidate.PublishedAtUtc)
                    .ThenByDescending(candidate => candidate.ActivityId)
                    .ToArray();
                pageCandidates = ordered.Take(request.PageSize).ToArray();
            }
            var budgetExhausted = push.Truncated || pull.Truncated;
            var hasMore = ordered.Length > request.PageSize || budgetExhausted;

            FeedSortTuple? highWatermark = cursor?.HighWatermark;
            if (highWatermark is null && candidates.Length > 0)
                highWatermark = ToTuple(candidates[0]);
            var boundary = pageCandidates.Length > 0
                ? ToTuple(pageCandidates[^1])
                : candidates.Length > 0
                    ? ToTuple(candidates[^1])
                    : (FeedSortTuple?)null;
            var nextCursor = hasMore && highWatermark.HasValue && boundary.HasValue
                ? FeedCursorCodec.Encode(
                    new FeedCursor(
                        snapshotAtUtc.Ticks,
                        highWatermark.Value,
                        boundary.Value),
                    viewerUserId,
                    revision,
                    request.PageSize,
                    request.IncludeSeen,
                    FeedCursorSurfaces.Items,
                    _cursorOptions,
                    timeProvider.GetUtcNow(),
                    _options.MaximumCursorLength)
                : null;
            var items = pageCandidates.Select(ToItem).ToArray();
            var containsPull = pageCandidates.Any(candidate =>
                candidate.MaterializationMode == FeedDeliveryModes.Pull);
            telemetry.RecordBatch("read_candidates", containsPull ? "hybrid" : "push", candidates.Length);
            var page = new FeedPage(items, nextCursor, hasMore, containsPull, revision);
            budget.EnsureResponseFits(page);
            return page;
        }

        private async Task<List<int>> LoadPullPublishersAsync(
            int viewerUserId,
            DateTime observedAtUtc,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            var cutoff = observedAtUtc.AddSeconds(-_options.MaximumCheckpointAgeSeconds);
            var retentionCutoff = observedAtUtc.AddDays(-_options.EntryRetentionDays);
            var publishers = await context.FeedAudienceEdges
                .AsNoTracking()
                .Where(edge => edge.ViewerUserId == viewerUserId
                    && !context.FeedUserErasures.Any(erasure =>
                        erasure.UserId == viewerUserId || erasure.UserId == edge.OwnerUserId)
                    && edge.State == RelationshipStates.Active
                    && (edge.RelationshipKind == RelationshipKinds.Follow
                        || edge.RelationshipKind == RelationshipKinds.Friend))
                .Join(
                    context.FeedRelationshipCheckpoints.AsNoTracking().Where(checkpoint =>
                        checkpoint.Status == AudienceProjectionStates.Healthy
                        && checkpoint.LastConfirmedAtUtc >= cutoff),
                    edge => edge.SourceStreamId,
                    checkpoint => (Guid?)checkpoint.StreamId,
                    (edge, checkpoint) => new { edge, checkpoint })
                .Where(pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion)
                .Where(pair => context.FeedActivities.Any(activity =>
                    activity.ActorUserId == pair.edge.OwnerUserId
                    && activity.DeliveryMode == FeedDeliveryModes.Pull
                    && !activity.IsTombstoned
                    && !context.FeedModerationDecisionStates.Any(decision =>
                        decision.ResourceKind == activity.TargetType
                        && decision.ResourceId == activity.TargetId
                        && decision.IsSuppressed)
                    && activity.PublishedAtUtc >= retentionCutoff))
                .Select(pair => pair.edge.OwnerUserId)
                .Distinct()
                .OrderBy(id => id)
                .Take(_options.MaximumPullPublishers + 1)
                .ToListAsync(cancellationToken);
            budget.Materialized(publishers.Count);
            if (publishers.Count > _options.MaximumPullPublishers)
            {
                publishers.RemoveAt(publishers.Count - 1);
                telemetry.Record("pull_publishers", "capped", FeedDeliveryModes.Pull);
            }
            budget.Processed(publishers.Count);
            return publishers;
        }

        private async Task<CandidateLoadResult> LoadPushCandidatesAsync(
            int viewerUserId,
            bool includeSeen,
            DateTime snapshotAtUtc,
            FeedCursor? cursor,
            int limit,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            budget.EnsureMaterializedCapacity(checked(limit + 1));
            var query = context.FeedEntries
                .AsNoTracking()
                .Where(entry => entry.RecipientUserId == viewerUserId
                    && !context.FeedUserErasures.Any(erasure =>
                        erasure.UserId == viewerUserId
                        || erasure.UserId == entry.Activity.ActorUserId)
                    && entry.RevokedAtUtc == null
                    && entry.MaterializationMode == FeedDeliveryModes.Push
                    && entry.Activity.IsTombstoned == false
                    && !context.FeedModerationDecisionStates.Any(decision =>
                        decision.ResourceKind == entry.Activity.TargetType
                        && decision.ResourceId == entry.Activity.TargetId
                        && decision.IsSuppressed)
                    && entry.CreatedAtUtc <= snapshotAtUtc
                    && entry.PublishedAtUtc <= snapshotAtUtc);
            if (!includeSeen)
                query = query.Where(entry => entry.SeenAtUtc == null);
            if (cursor is not null)
            {
                var high = cursor.HighWatermark;
                var last = cursor.Last;
                var highPublished = new DateTime(high.PublishedUtcTicks, DateTimeKind.Utc);
                var lastPublished = new DateTime(last.PublishedUtcTicks, DateTimeKind.Utc);
                query = query.Where(entry =>
                    (entry.RankKey < high.RankKey
                        || (entry.RankKey == high.RankKey
                            && (entry.PublishedAtUtc < highPublished
                                || (entry.PublishedAtUtc == highPublished
                                    && entry.ActivityId <= high.ActivityId))))
                    && (entry.RankKey < last.RankKey
                        || (entry.RankKey == last.RankKey
                            && (entry.PublishedAtUtc < lastPublished
                                || (entry.PublishedAtUtc == lastPublished
                                    && entry.ActivityId < last.ActivityId)))));
            }
            var rows = await query
                .OrderByDescending(entry => entry.RankKey)
                .ThenByDescending(entry => entry.PublishedAtUtc)
                .ThenByDescending(entry => entry.ActivityId)
                .Take(limit + 1)
                .Select(entry => new Candidate(
                    entry.Id,
                    entry.ActivityId,
                    entry.Activity.TargetType,
                    entry.Activity.TargetId,
                    entry.Activity.ActivityKind,
                    entry.Activity.ActorUserId,
                    entry.Activity.AudiencePolicy,
                    entry.Activity.Summary,
                    entry.RankKey,
                    entry.PublishedAtUtc,
                    entry.SeenAtUtc,
                    FeedDeliveryModes.Push))
                .ToListAsync(cancellationToken);
            budget.Materialized(rows.Count);
            var truncated = rows.Count > limit;
            var excludedFrontier = truncated ? ToTuple(rows[limit]) : (FeedSortTuple?)null;
            if (truncated)
                rows.RemoveAt(rows.Count - 1);
            return new CandidateLoadResult(rows, truncated, excludedFrontier);
        }

        private async Task<CandidateLoadResult> LoadPullCandidatesAsync(
            int viewerUserId,
            IReadOnlyCollection<int> publisherIds,
            bool includeSeen,
            DateTime snapshotAtUtc,
            FeedCursor? cursor,
            int limit,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            var parameters = new List<MySqlParameter>();
            var publisherParameters = new List<string>();
            foreach (var publisherId in publisherIds)
            {
                var name = $"@publisher{publisherParameters.Count}";
                publisherParameters.Add(name);
                parameters.Add(new MySqlParameter(name, publisherId));
            }
            parameters.Add(new MySqlParameter("@snapshot", snapshotAtUtc));
            parameters.Add(new MySqlParameter(
                "@retentionCutoff",
                snapshotAtUtc.AddDays(-_options.EntryRetentionDays)));
            parameters.Add(new MySqlParameter("@viewer", viewerUserId));
            parameters.Add(new MySqlParameter("@includeSeen", includeSeen));
            var boundarySql = string.Empty;
            if (cursor is not null)
            {
                parameters.Add(new MySqlParameter("@highRank", cursor.HighWatermark.RankKey));
                parameters.Add(new MySqlParameter("@highPublished", new DateTime(cursor.HighWatermark.PublishedUtcTicks, DateTimeKind.Utc)));
                parameters.Add(new MySqlParameter("@highId", cursor.HighWatermark.ActivityId));
                parameters.Add(new MySqlParameter("@lastRank", cursor.Last.RankKey));
                parameters.Add(new MySqlParameter("@lastPublished", new DateTime(cursor.Last.PublishedUtcTicks, DateTimeKind.Utc)));
                parameters.Add(new MySqlParameter("@lastId", cursor.Last.ActivityId));
                boundarySql = """
                    AND (activity.`RankKey` < @highRank
                        OR (activity.`RankKey` = @highRank
                            AND (activity.`PublishedAtUtc` < @highPublished
                                OR (activity.`PublishedAtUtc` = @highPublished AND activity.`Id` <= @highId))))
                    AND (activity.`RankKey` < @lastRank
                        OR (activity.`RankKey` = @lastRank
                            AND (activity.`PublishedAtUtc` < @lastPublished
                                OR (activity.`PublishedAtUtc` = @lastPublished AND activity.`Id` < @lastId))))
                    """;
            }
            var perPublisher = FeedReadPolicy.PerPublisherCandidateLimit(
                limit,
                publisherIds.Count,
                _options.MaximumCandidatesPerPullPublisher);
            var perPublisherProbe = checked(perPublisher + 1);
            var probeLimit = checked(perPublisherProbe * publisherIds.Count);
            budget.EnsureMaterializedCapacity(probeLimit);
            parameters.Add(new MySqlParameter("@perPublisherProbe", perPublisherProbe));
            parameters.Add(new MySqlParameter("@probeLimit", probeLimit));
            var boundedPublisherQueries = publisherParameters.Select(publisherParameter => $"""
                (SELECT COALESCE(marker.`Id`, 0) AS `EntryId`, activity.`Id` AS `ActivityId`, activity.`TargetType`,
                        activity.`TargetId`, activity.`ActivityKind`, activity.`ActorUserId`,
                        activity.`AudiencePolicy`, activity.`Summary`, activity.`RankKey`,
                        activity.`PublishedAtUtc`, marker.`SeenAtUtc`, 'pull' AS `MaterializationMode`,
                        1 AS `FairRow`
                 FROM `FeedActivity` AS activity
                 LEFT JOIN `FeedEntry` AS marker
                   ON marker.`RecipientUserId` = @viewer
                  AND marker.`ActivityId` = activity.`Id`
                  AND marker.`MaterializationMode` = 'pull'
                 WHERE activity.`ActorUserId` = {publisherParameter}
                   AND NOT EXISTS (
                       SELECT 1 FROM `FeedUserErasure` AS erasure
                       WHERE erasure.`UserId` = activity.`ActorUserId`
                          OR erasure.`UserId` = @viewer)
                   AND activity.`DeliveryMode` = 'pull'
                   AND activity.`IsTombstoned` = 0
                   AND NOT EXISTS (
                       SELECT 1 FROM `FeedModerationDecisionState` AS moderation
                       WHERE moderation.`ResourceKind` = activity.`TargetType`
                         AND moderation.`ResourceId` = activity.`TargetId`
                         AND moderation.`IsSuppressed` = 1)
                   AND activity.`CreatedAtUtc` <= @snapshot
                   AND activity.`PublishedAtUtc` >= @retentionCutoff
                   AND activity.`PublishedAtUtc` <= @snapshot
                   AND (@includeSeen = 1 OR marker.`SeenAtUtc` IS NULL)
                   {boundarySql}
                 ORDER BY activity.`RankKey` DESC,
                          activity.`PublishedAtUtc` DESC,
                          activity.`Id` DESC
                 LIMIT @perPublisherProbe)
                """);
            var sql = $"""
                SELECT bounded.`EntryId`, bounded.`ActivityId`, bounded.`TargetType`,
                       bounded.`TargetId`, bounded.`ActivityKind`, bounded.`ActorUserId`,
                       bounded.`AudiencePolicy`, bounded.`Summary`, bounded.`RankKey`,
                       bounded.`PublishedAtUtc`, bounded.`SeenAtUtc`, bounded.`MaterializationMode`,
                       bounded.`FairRow`
                FROM (
                    {string.Join(" UNION ALL ", boundedPublisherQueries)}
                ) AS bounded
                ORDER BY bounded.`RankKey` DESC,
                         bounded.`PublishedAtUtc` DESC,
                         bounded.`ActivityId` DESC
                LIMIT @probeLimit
                """;
            var rows = await context.FeedPullCandidateRows
                .FromSqlRaw(sql, parameters.Cast<object>().ToArray())
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            budget.Materialized(rows.Count);
            budget.Processed(rows.Count);
            FeedFairSelection<FeedPullCandidateRow> fairRows;
            Candidate[] selected;
            using (budget.MeasureMerge())
            {
                fairRows = FeedReadPolicy.SelectPerPublisher(
                    rows,
                    row => row.ActorUserId,
                    perPublisher,
                    limit);
                selected = fairRows.Items.Select(row => new Candidate(
                    row.EntryId,
                    row.ActivityId,
                    row.TargetType,
                    row.TargetId,
                    row.ActivityKind,
                    row.ActorUserId,
                    row.AudiencePolicy,
                    row.Summary,
                    row.RankKey,
                    row.PublishedAtUtc,
                    row.SeenAtUtc,
                    row.MaterializationMode)).ToArray();
            }
            var excludedFrontier = fairRows.ExcludedItems.Count == 0
                ? (FeedSortTuple?)null
                : fairRows.ExcludedItems
                    .Select(row => new FeedSortTuple(
                        row.RankKey,
                        row.PublishedAtUtc.Ticks,
                        row.ActivityId))
                    .OrderByDescending(tuple => tuple)
                    .First();
            return new CandidateLoadResult(selected, fairRows.Truncated, excludedFrontier);
        }

        private async Task<List<Candidate>> AuthorizeAsync(
            int viewerUserId,
            IReadOnlyCollection<Candidate> candidates,
            DateTime evaluatedAtUtc,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            if (candidates.Count == 0)
                return [];
            var ownerCandidates = candidates
                .Where(item => item.ActorUserId == viewerUserId)
                .ToList();
            var nonOwnerCandidates = candidates
                .Where(item => item.ActorUserId != viewerUserId)
                .ToArray();
            if (nonOwnerCandidates.Length == 0)
                return ownerCandidates;

            using var authorizationDeadline = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            authorizationDeadline.CancelAfter(TimeSpan.FromMilliseconds(
                _audienceOptions.AuthorizationDeadlineMilliseconds));
            try
            {
                var authorized = await AuthorizeNonOwnerAsync(
                    viewerUserId,
                    nonOwnerCandidates,
                    ownerCandidates,
                    evaluatedAtUtc,
                    budget,
                    authorizationDeadline.Token);
                var authorizedActivityIds = authorized
                    .Select(item => item.ActivityId)
                    .ToHashSet();
                return candidates
                    .Where(item => authorizedActivityIds.Contains(item.ActivityId))
                    .ToList();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                && authorizationDeadline.IsCancellationRequested)
            {
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.AudienceProjection);
            }
        }

        private async Task<List<Candidate>> AuthorizeNonOwnerAsync(
            int viewerUserId,
            IReadOnlyCollection<Candidate> candidates,
            List<Candidate> result,
            DateTime evaluatedAtUtc,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            var bootstrap = await context.FeedAudienceBootstrapStates
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    state => state.Id == FeedAudienceBootstrapState.SingletonId,
                    cancellationToken);
            budget.Materialized(bootstrap is null ? 0 : 1);
            if (bootstrap is null
                || !bootstrap.IsCompleteAndFresh(
                    evaluatedAtUtc,
                    TimeSpan.FromSeconds(_audienceOptions.MaximumBootstrapAgeSeconds)))
            {
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.AudienceProjection);
            }

            var actorIds = candidates.Select(item => item.ActorUserId).Distinct().ToArray();
            budget.Processed(actorIds.Length);
            var checkpoints = await context.FeedRelationshipCheckpoints
                .AsNoTracking()
                .Where(item => (actorIds.Contains(item.OwnerUserId)
                        && item.ViewerUserId == viewerUserId)
                    || (item.OwnerUserId == viewerUserId
                        && actorIds.Contains(item.ViewerUserId)
                        && item.RelationshipKind == RelationshipKinds.Block))
                .Take(budget.RemainingMaterializedRows + 1)
                .ToListAsync(cancellationToken);
            budget.Materialized(checkpoints.Count);
            var edges = await context.FeedAudienceEdges
                .AsNoTracking()
                .Where(item => (actorIds.Contains(item.OwnerUserId)
                        && item.ViewerUserId == viewerUserId)
                    || (item.OwnerUserId == viewerUserId
                        && actorIds.Contains(item.ViewerUserId)
                        && item.RelationshipKind == RelationshipKinds.Block))
                .Take(budget.RemainingMaterializedRows + 1)
                .ToListAsync(cancellationToken);
            budget.Materialized(edges.Count);
            budget.Processed(checkpoints.Count + edges.Count + candidates.Count);
            var cutoff = evaluatedAtUtc.AddSeconds(-_options.MaximumCheckpointAgeSeconds);
            Dictionary<RelationshipKey, FeedRelationshipCheckpoint> checkpointsByKey;
            Dictionary<RelationshipKey, FeedAudienceEdge> edgesByKey;
            using (budget.MeasureMerge())
            {
                checkpointsByKey = checkpoints
                    .GroupBy(item => new RelationshipKey(
                        item.OwnerUserId,
                        item.ViewerUserId,
                        item.RelationshipKind))
                    .ToDictionary(group => group.Key, group => group
                        .OrderByDescending(item => item.LastAppliedVersion)
                        .First());
                edgesByKey = edges
                    .GroupBy(item => new RelationshipKey(
                        item.OwnerUserId,
                        item.ViewerUserId,
                        item.RelationshipKind))
                    .ToDictionary(group => group.Key, group => group
                        .OrderByDescending(item => item.SourceVersion)
                        .First());
            }
            using (budget.MeasureMerge())
            {
                var processed = 0;
                foreach (var candidate in candidates)
                {
                    if ((processed++ & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (!BlocksAllow(
                        candidate.ActorUserId,
                        viewerUserId,
                        checkpointsByKey,
                        edgesByKey,
                        cutoff))
                        continue;
                    if (candidate.AudiencePolicy == FeedAudiencePolicies.Public)
                    {
                        result.Add(candidate);
                        continue;
                    }
                    var requiredKind = candidate.AudiencePolicy switch
                    {
                        FeedAudiencePolicies.Followers => RelationshipKinds.Follow,
                        FeedAudiencePolicies.Friends => RelationshipKinds.Friend,
                        FeedAudiencePolicies.FriendsOfFriends => RelationshipKinds.Friend,
                        _ => null,
                    };
                    if (requiredKind is null)
                        continue;
                    var key = new RelationshipKey(
                        candidate.ActorUserId,
                        viewerUserId,
                        requiredKind);
                    checkpointsByKey.TryGetValue(key, out var checkpoint);
                    edgesByKey.TryGetValue(key, out var edge);
                    if (checkpoint is not null
                        && edge is not null
                        && checkpoint.Status == AudienceProjectionStates.Healthy
                        && checkpoint.LastConfirmedAtUtc >= cutoff
                        && edge.SourceStreamId == checkpoint.StreamId
                        && edge.SourceVersion == checkpoint.LastAppliedVersion
                        && edge.State == RelationshipStates.Active)
                    {
                        result.Add(candidate);
                    }
                }
            }

            var alreadyAllowedIds = result.Select(item => item.ActivityId).ToHashSet();
            var friendsOfFriendsActors = candidates
                .Where(item => item.AudiencePolicy == FeedAudiencePolicies.FriendsOfFriends
                    && !alreadyAllowedIds.Contains(item.ActivityId))
                .Select(item => item.ActorUserId)
                .Distinct()
                .ToArray();
            if (friendsOfFriendsActors.Length == 0)
                return result;

            var firstHops = await HealthyFriendEdges(cutoff)
                .Where(edge => friendsOfFriendsActors.Contains(edge.OwnerUserId)
                    && edge.ViewerUserId != viewerUserId
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == edge.OwnerUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == edge.OwnerUserId))))
                .Select(edge => new
                {
                    ActorUserId = edge.OwnerUserId,
                    IntermediateUserId = edge.ViewerUserId,
                })
                .Distinct()
                .OrderBy(item => item.ActorUserId)
                .ThenBy(item => item.IntermediateUserId)
                .Take(_audienceOptions.MaximumFriendsOfFriendsCandidates + 1)
                .ToListAsync(cancellationToken);
            budget.Materialized(firstHops.Count);
            budget.Processed(firstHops.Count);
            if (firstHops.Count > _audienceOptions.MaximumFriendsOfFriendsCandidates)
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.ProcessedRows);
            if (firstHops.Count == 0)
                return result;

            var intermediates = firstHops
                .Select(item => item.IntermediateUserId)
                .Distinct()
                .ToArray();
            var healthySecondHops = await HealthyFriendEdges(cutoff)
                .Where(edge => intermediates.Contains(edge.OwnerUserId)
                    && edge.ViewerUserId == viewerUserId
                    && !context.FeedAudienceEdges.Any(block =>
                        block.RelationshipKind == RelationshipKinds.Block
                        && block.State == RelationshipStates.Blocked
                        && ((block.OwnerUserId == edge.OwnerUserId
                                && block.ViewerUserId == viewerUserId)
                            || (block.OwnerUserId == viewerUserId
                                && block.ViewerUserId == edge.OwnerUserId))))
                .Select(edge => edge.OwnerUserId)
                .Distinct()
                .OrderBy(userId => userId)
                .Take(_audienceOptions.MaximumFriendsOfFriendsCandidates + 1)
                .ToListAsync(cancellationToken);
            budget.Materialized(healthySecondHops.Count);
            budget.Processed(healthySecondHops.Count);
            if (healthySecondHops.Count > _audienceOptions.MaximumFriendsOfFriendsCandidates)
                throw new FeedReadBudgetExceededException(FeedReadBudgetReasons.ProcessedRows);

            var secondHopSet = healthySecondHops.ToHashSet();
            var allowedActors = firstHops
                .Where(item => secondHopSet.Contains(item.IntermediateUserId))
                .Select(item => item.ActorUserId)
                .ToHashSet();
            foreach (var candidate in candidates)
            {
                if (candidate.AudiencePolicy == FeedAudiencePolicies.FriendsOfFriends
                    && !alreadyAllowedIds.Contains(candidate.ActivityId)
                    && allowedActors.Contains(candidate.ActorUserId))
                {
                    result.Add(candidate);
                }
            }
            return result;
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

        private static bool BlocksAllow(
            int actorUserId,
            int viewerUserId,
            IReadOnlyDictionary<RelationshipKey, FeedRelationshipCheckpoint> checkpoints,
            IReadOnlyDictionary<RelationshipKey, FeedAudienceEdge> edges,
            DateTime cutoff)
        {
            var directions = new[]
            {
                new RelationshipKey(actorUserId, viewerUserId, RelationshipKinds.Block),
                new RelationshipKey(viewerUserId, actorUserId, RelationshipKinds.Block),
            };
            foreach (var key in directions)
            {
                checkpoints.TryGetValue(key, out var checkpoint);
                edges.TryGetValue(key, out var edge);
                if (checkpoint is null && edge is null)
                    continue;
                if (checkpoint is null
                    || edge is null
                    || checkpoint.Status != AudienceProjectionStates.Healthy
                    || checkpoint.LastConfirmedAtUtc < cutoff
                    || edge.SourceStreamId != checkpoint.StreamId
                    || edge.SourceVersion != checkpoint.LastAppliedVersion
                    || edge.State == RelationshipStates.Blocked)
                {
                    return false;
                }
            }
            return true;
        }

        private static FeedItem ToItem(Candidate candidate) =>
            new(
                candidate.EntryId,
                candidate.ActivityId,
                candidate.TargetType,
                candidate.TargetId,
                candidate.ActivityKind,
                candidate.ActorUserId,
                candidate.AudiencePolicy,
                candidate.Summary,
                candidate.RankKey,
                candidate.PublishedAtUtc,
                candidate.SeenAtUtc,
                candidate.MaterializationMode,
                null);

        private async Task<FeedPage> ApplyModerationSuppressionAsync(
            FeedPage page,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            if (page.Items.Count == 0)
                return page;

            var resourceTypes = page.Items
                .Select(item => item.TargetType)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var resourceIds = page.Items
                .Select(item => item.TargetId)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (resourceTypes.Length == 0 || resourceIds.Length == 0)
                return page;

            var maximumCoordinates = checked(resourceTypes.Length * resourceIds.Length);
            budget.EnsureMaterializedCapacity(checked(maximumCoordinates + 1));
            var suppressedRows = await context.FeedModerationDecisionStates
                .AsNoTracking()
                .Where(state => state.IsSuppressed
                    && resourceTypes.Contains(state.ResourceKind)
                    && resourceIds.Contains(state.ResourceId))
                .Select(state => new { state.ResourceKind, state.ResourceId })
                .Distinct()
                .Take(maximumCoordinates + 1)
                .ToListAsync(cancellationToken);
            budget.Materialized(suppressedRows.Count);
            if (suppressedRows.Count == 0)
                return page;

            var suppressed = suppressedRows
                .Select(row => ResourceKey(row.ResourceKind, row.ResourceId))
                .ToHashSet(StringComparer.Ordinal);
            var visible = page.Items
                .Where(item => !suppressed.Contains(ResourceKey(item.TargetType, item.TargetId)))
                .ToArray();
            budget.Processed(page.Items.Count + suppressed.Count);
            return page with
            {
                Items = visible,
                ContainsPullItems = visible.Any(item =>
                    item.MaterializationMode == FeedDeliveryModes.Pull),
            };
        }

        private async Task<FeedPage> HydrateEngagementAsync(
            FeedPage page,
            FeedReadWorkBudget budget,
            CancellationToken cancellationToken)
        {
            if (page.Items.Count == 0)
                return page;

            var resourceIds = page.Items
                .Where(item => item.TargetType is FeedEngagementResourceTypes.Comment
                    or FeedEngagementResourceTypes.Post)
                .Where(item => !string.IsNullOrWhiteSpace(item.TargetId)
                    && item.TargetId.Length <= FeedEngagementResourceTypes.MaximumIdLength)
                .Select(item => item.TargetId)
                .Distinct(StringComparer.Ordinal)
                .Take(_options.MaximumPageSize)
                .ToArray();
            if (resourceIds.Length == 0)
                return page;

            var summaries = await context.FeedEngagementSummaries
                .AsNoTracking()
                .Where(item => resourceIds.Contains(item.ResourceId)
                    && (item.ResourceType == FeedEngagementResourceTypes.Comment
                        || item.ResourceType == FeedEngagementResourceTypes.Post)
                    && item.Status == FeedEngagementProjectionStates.Healthy
                    && item.LastAppliedVersion > 0
                    && item.LastAppliedVersion == item.HighestObservedVersion
                    && item.LastSourceOccurredAtUtc != null)
                .Select(item => new
                {
                    item.ResourceType,
                    item.ResourceId,
                    Summary = new EngagementSummaryDto(
                        item.LikeCount,
                        item.DislikeCount,
                        item.RatingCount,
                        item.RatingTotal,
                        item.ViewCount,
                        item.CommentCount,
                        item.ShareCount,
                        item.LastAppliedVersion,
                        item.LastSourceOccurredAtUtc.GetValueOrDefault()),
                })
                .Take(budget.RemainingMaterializedRows + 1)
                .ToListAsync(cancellationToken);
            budget.Materialized(summaries.Count);
            budget.Processed(page.Items.Count + summaries.Count);
            FeedItem[] hydrated;
            using (budget.MeasureMerge())
            {
                var byResource = summaries.ToDictionary(
                    item => ResourceKey(item.ResourceType, item.ResourceId),
                    item => item.Summary,
                    StringComparer.Ordinal);
                hydrated = page.Items
                    .Select(item => item with
                    {
                        Engagement = byResource.GetValueOrDefault(
                            ResourceKey(item.TargetType, item.TargetId)),
                    })
                    .ToArray();
            }
            return page with { Items = hydrated };
        }

        private static string ResourceKey(string resourceType, string resourceId) =>
            $"{resourceType}\u001f{resourceId}";

        private static FeedSortTuple ToTuple(Candidate candidate) =>
            new(candidate.RankKey, candidate.PublishedAtUtc.Ticks, candidate.ActivityId);

        private static FeedSortTuple? HighestFrontier(
            FeedSortTuple? first,
            FeedSortTuple? second)
        {
            if (!first.HasValue)
                return second;
            if (!second.HasValue)
                return first;
            return first.Value.CompareTo(second.Value) >= 0 ? first : second;
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private sealed record Candidate(
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
            string MaterializationMode);

        private sealed record CandidateLoadResult(
            IReadOnlyList<Candidate> Items,
            bool Truncated,
            FeedSortTuple? ExcludedFrontier);

        private readonly record struct RelationshipKey(
            int OwnerUserId,
            int ViewerUserId,
            string RelationshipKind);
    }
}
