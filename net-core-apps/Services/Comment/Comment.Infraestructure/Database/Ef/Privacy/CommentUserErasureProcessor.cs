using Comment.Application.Features.Privacy;
using Comment.Application.Providers.Storage;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Interactions;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.AggregatesModel.Privacy;
using Comment.Domain.AggregatesModel.Visibility;
using Comment.Domain.Exceptions;
using MessageBrokerContracts.Privacy.Restricted;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Privacy;

public sealed class CommentUserErasureProcessor(
    CommentContext context,
    ICommentMediaObjectStore objects,
    ICommentPrivacyEventPublisher privacyEvents,
    IOptions<CommentUserErasureOptions> configuredOptions,
    TimeProvider timeProvider,
    CommentUserErasureTelemetry telemetry) : ICommentUserErasureProcessor
{
    private readonly CommentUserErasureOptions _options = configuredOptions.Value;

    public async Task<CommentUserErasureCycleResult> ProcessCycleAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        ValidateWorker(workerId);
        var claim = await ClaimAsync(workerId, cancellationToken);
        if (claim is null)
        {
            telemetry.Cycle("idle");
            return new(false, false, false, false, 0, 0);
        }

        var stopwatch = Stopwatch.StartNew();
        var batches = 0;
        var rows = 0;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.CycleTimeBudget);
        try
        {
            while (batches < _options.MaximumBatchesPerCycle
                && rows < _options.MaximumRowsPerCycle
                && stopwatch.Elapsed < _options.CycleTimeBudget)
            {
                var batch = await ProcessBatchAsync(
                    claim.UserId,
                    claim.ErasureVersion,
                    workerId,
                    claim.FencingToken,
                    Math.Min(_options.BatchSize, _options.MaximumRowsPerCycle - rows),
                    budget.Token);
                if (batch.LeaseLost)
                {
                    telemetry.Cycle("lease_lost");
                    return new(true, false, false, true, batches, rows);
                }

                batches++;
                rows += batch.Rows;
                telemetry.Row(batch.Stage, batch.Rows);
                if (batch.Completed)
                {
                    telemetry.Cycle("completed");
                    return new(true, true, false, false, batches, rows);
                }
                if (batch.Blocked)
                {
                    telemetry.Cycle("blocked");
                    return new(true, false, true, false, batches, rows);
                }
                if (batch.Stop)
                {
                    telemetry.Cycle("retry");
                    return new(true, false, false, false, batches, rows);
                }
            }

            await ReleaseAsync(claim, workerId, "cycle_budget", cancellationToken);
            telemetry.Cycle("yielded");
            return new(true, false, false, false, batches, rows);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            telemetry.Cycle("time_budget");
            return new(true, false, false, false, batches, rows);
        }
    }

    private async Task<Claim?> ClaimAsync(string workerId, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var now = UtcNow;
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var erasure = await context.CommentUserErasures.FromSqlInterpolated($"""
            SELECT * FROM `CommentUserErasure`
            WHERE `Status` IN ({(byte)CommentUserErasureStatus.Pending}, {(byte)CommentUserErasureStatus.Processing})
              AND (`LeaseUntilUtc` IS NULL OR `LeaseUntilUtc` <= {now})
            ORDER BY `RequestedAtUtc`, `UserId`
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);
        if (erasure is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var token = erasure.Claim(workerId, now, _options.LeaseDuration);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(erasure.UserId, erasure.ErasureVersion, token);
    }

    private Task<BatchResult> ProcessBatchAsync(
        int userId,
        long erasureVersion,
        string workerId,
        long fencingToken,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        if (maximumRows <= 0)
            return Task.FromResult(BatchResult.StopNow("budget"));
        return ProcessBatchCoreAsync(
            userId, erasureVersion, workerId, fencingToken, maximumRows, cancellationToken);
    }

    private async Task<BatchResult> ProcessBatchCoreAsync(
        int userId,
        long erasureVersion,
        string workerId,
        long fencingToken,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var snapshot = await context.CommentUserErasures.AsNoTracking()
            .SingleAsync(value => value.UserId == userId, cancellationToken);
        if (!snapshot.OwnsLease(workerId, fencingToken, erasureVersion, UtcNow))
            return BatchResult.Lost;
        if (snapshot.Stage == CommentUserErasureStage.MediaCleanup)
            return await ProcessMediaCleanupAsync(
                snapshot, workerId, fencingToken, maximumRows, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var job = await context.CommentUserErasures.FromSqlInterpolated($"""
            SELECT * FROM `CommentUserErasure`
            WHERE `UserId` = {userId}
            FOR UPDATE
            """).SingleAsync(cancellationToken);
        var now = UtcNow;
        if (!job.OwnsLease(workerId, fencingToken, erasureVersion, now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BatchResult.Lost;
        }

        try
        {
            if (job.Stage == CommentUserErasureStage.Complete)
            {
                var eventId = CommentUserErasureEventIds.Create(
                    job.RequestId, job.ErasureVersion, UserErasureOutcomes.Completed);
                await privacyEvents.StageCompletionAsync(new UserErasureCompleted
                {
                    EventId = eventId,
                    RequestId = job.RequestId,
                    UserId = job.UserId,
                    ErasureVersion = job.ErasureVersion,
                    Service = "comment",
                    Outcome = UserErasureOutcomes.Completed,
                    CompletedAtUtc = new DateTimeOffset(now, TimeSpan.Zero),
                }, cancellationToken);
                job.Complete(eventId, workerId, fencingToken, erasureVersion, now);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(0, true, false, false, false, "complete");
            }

            var page = await ProcessStageAsync(job, maximumRows, now, cancellationToken);
            job.Advance(page.NextStage, page.NextCursor, workerId, fencingToken,
                erasureVersion, now, _options.LeaseDuration);
            await context.SaveEntitiesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(page.Rows, false, false, false, false,
                job.Stage.ToString().ToLowerInvariant());
        }
        catch (AmbiguousMediaOwnershipException)
        {
            context.ChangeTracker.Clear();
            var fenced = await context.CommentUserErasures.FromSqlInterpolated($"""
                SELECT * FROM `CommentUserErasure` WHERE `UserId` = {userId} FOR UPDATE
                """).SingleAsync(cancellationToken);
            fenced.Block("ambiguous_media_reference", workerId, fencingToken,
                erasureVersion, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(0, false, true, false, false, "owned_comments");
        }
        catch (DbUpdateConcurrencyException)
        {
            // Rollback must outlive a canceled request, while remaining bounded.
            using var rollbackTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(rollbackTimeout.Token);
            return BatchResult.Lost;
        }
    }

    private Task<StagePage> ProcessStageAsync(
        CommentUserErasure job,
        int maximumRows,
        DateTime now,
        CancellationToken cancellationToken) => job.Stage switch
        {
            CommentUserErasureStage.MediaDiscovery =>
                DiscoverMediaAsync(job, maximumRows, now, cancellationToken),
            CommentUserErasureStage.OwnedComments =>
                RemoveOwnedCommentsAsync(job, maximumRows, cancellationToken),
            CommentUserErasureStage.Mentions =>
                RemoveMentionsAsync(job, maximumRows, cancellationToken),
            CommentUserErasureStage.RelationshipEdges =>
                RemoveRelationshipEdgesAsync(job, maximumRows, cancellationToken),
            CommentUserErasureStage.RelationshipCheckpoints =>
                RemoveRelationshipCheckpointsAsync(job, maximumRows, cancellationToken),
            CommentUserErasureStage.RelationshipQuarantine =>
                RemoveRelationshipQuarantineAsync(job, maximumRows, cancellationToken),
            CommentUserErasureStage.ActorProjections =>
                RemoveActorProjectionsAsync(job, maximumRows, cancellationToken),
            CommentUserErasureStage.MediaRows =>
                RemoveMediaRowsAsync(job, maximumRows, cancellationToken),
            _ => throw new InvalidOperationException("Unknown Comment user-erasure stage."),
        };

    private async Task<StagePage> DiscoverMediaAsync(
        CommentUserErasure job,
        int maximumRows,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await context.CommentMediaIngestions
            .Include(value => value.Items)
            .Where(value => value.OwnerUserId == job.UserId && value.Id > job.Cursor)
            .OrderBy(value => value.Id)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);
        foreach (var ingestion in rows)
        {
            foreach (var item in ingestion.Items)
            {
                EnsureExactObjectNames(ingestion, item);
                if (item.PromotedAtUtc.HasValue && item.CleanedAtUtc is null)
                {
                    var finalName = item.FinalObjectName;
                    var finalVersion = item.FinalObjectVersion;
                    if (string.IsNullOrWhiteSpace(finalName)
                        || string.IsNullOrWhiteSpace(finalVersion))
                        throw new AmbiguousMediaOwnershipException();
                    await AddMediaWorkAsync(
                        ingestion,
                        item,
                        "final",
                        finalName,
                        finalVersion,
                        now,
                        cancellationToken);
                }
                if (item.QuarantinedAtUtc.HasValue && item.QuarantineDeletedAtUtc is null
                    && item.CleanedAtUtc is null)
                {
                    var quarantineVersion = item.QuarantineObjectVersion;
                    if (string.IsNullOrWhiteSpace(quarantineVersion))
                        throw new AmbiguousMediaOwnershipException();
                    await AddMediaWorkAsync(
                        ingestion,
                        item,
                        "quarantine",
                        item.QuarantineObjectName,
                        quarantineVersion,
                        now,
                        cancellationToken);
                }
            }
        }
        return Advance(job, rows, value => value.Id);
    }

    private async Task AddMediaWorkAsync(
        CommentMediaIngestion ingestion,
        CommentMediaIngestionItem item,
        string kind,
        string name,
        string version,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (await context.CommentErasureMediaObjects.AnyAsync(value =>
            value.UserId == ingestion.OwnerUserId && value.ObjectKind == kind
            && value.ObjectName == name, cancellationToken))
            return;
        context.CommentErasureMediaObjects.Add(CommentErasureMediaObject.Create(
            ingestion.OwnerUserId, ingestion.Id, kind, name, version, now));
    }

    private async Task<StagePage> RemoveOwnedCommentsAsync(
        CommentUserErasure job,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var ids = await context.Comments.IgnoreQueryFilters().AsNoTracking()
            .Where(value => value.AuthorUserId == job.UserId && value.Id > job.Cursor)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .Take(maximumRows)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
            return Next(job);

        var comments = await context.Comments.IgnoreQueryFilters()
            .Where(value => ids.Contains(value.Id))
            .Include(value => value.MediaItems)
            .Include(value => value.Categories)
            .Include(value => value.Mentions)
            .AsSplitQuery()
            .OrderBy(value => value.Id)
            .ToListAsync(cancellationToken);
        var ingestions = await context.CommentMediaIngestions
            .Include(value => value.Items)
            .Where(value => value.OwnerUserId == job.UserId
                && value.BoundCommentId != null && ids.Contains(value.BoundCommentId.Value))
            .ToListAsync(cancellationToken);
        var mediaWork = await context.CommentErasureMediaObjects.AsNoTracking()
            .Where(value => value.UserId == job.UserId && value.ObjectKind == "final")
            .Select(value => value.ObjectName)
            .ToArrayAsync(cancellationToken);
        var knownObjects = mediaWork.ToHashSet(StringComparer.Ordinal);

        foreach (var comment in comments)
        {
            foreach (var media in comment.MediaItems)
            {
                if (!string.IsNullOrWhiteSpace(media.ThumbnailUrl))
                    throw new AmbiguousMediaOwnershipException();
                var matches = ingestions
                    .Where(value => value.BoundCommentId == comment.Id)
                    .SelectMany(value => value.Items)
                    .Where(value => string.Equals(value.PrivateObjectUrl, media.Url,
                        StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1)
                    throw new AmbiguousMediaOwnershipException();
                var finalObjectName = matches[0].FinalObjectName;
                if (string.IsNullOrWhiteSpace(finalObjectName)
                    || !knownObjects.Contains(finalObjectName))
                    throw new AmbiguousMediaOwnershipException();
            }

            comment.PrepareForRemoval(job.UserId);
            context.Comments.Remove(comment);
        }

        // Persist/dequeue removal events before deleting the comment rows.
        await context.SaveEntitiesAsync(cancellationToken);

        await context.CommentInteractionSummaryGaps
            .Where(value => ids.Contains(value.CommentId)).ExecuteDeleteAsync(cancellationToken);
        await context.CommentInteractionSummaryProjections
            .Where(value => ids.Contains(value.CommentId)).ExecuteDeleteAsync(cancellationToken);
        await context.CommentInteractionActorProjections
            .Where(value => ids.Contains(value.CommentId)).ExecuteDeleteAsync(cancellationToken);
        return new(job.Stage, ids[^1], ids.Length);
    }

    private async Task<StagePage> RemoveMentionsAsync(
        CommentUserErasure job, int maximumRows, CancellationToken cancellationToken)
    {
        // UserMention is an EF owned collection and intentionally has no
        // standalone DbSet. Delete a stable bounded prefix; repeated batches
        // converge without an offset that could skip rows after each delete.
        var removed = await context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM `CommentMentions`
            WHERE `UserId` = {job.UserId}
            ORDER BY `CommentPostId`, `Id`
            LIMIT {maximumRows};
            """, cancellationToken);
        return removed == 0
            ? Next(job)
            : new StagePage(job.Stage, job.Cursor, removed);
    }

    private async Task<StagePage> RemoveRelationshipEdgesAsync(
        CommentUserErasure job, int maximumRows, CancellationToken cancellationToken)
    {
        var rows = await context.CommentRelationshipEdges
            .Where(value => value.Id > job.Cursor
                && (value.OwnerUserId == job.UserId || value.ViewerUserId == job.UserId))
            .OrderBy(value => value.Id).Take(maximumRows).ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return Next(job);
        context.CommentRelationshipEdges.RemoveRange(rows);
        return new(job.Stage, rows[^1].Id, rows.Count);
    }

    private async Task<StagePage> RemoveRelationshipCheckpointsAsync(
        CommentUserErasure job, int maximumRows, CancellationToken cancellationToken)
    {
        var rows = await context.CommentRelationshipCheckpoints
            .Where(value => value.OwnerUserId == job.UserId || value.ViewerUserId == job.UserId)
            .Take(maximumRows).ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return Next(job);
        context.CommentRelationshipCheckpoints.RemoveRange(rows);
        return new(job.Stage, job.Cursor, rows.Count);
    }

    private async Task<StagePage> RemoveRelationshipQuarantineAsync(
        CommentUserErasure job, int maximumRows, CancellationToken cancellationToken)
    {
        var rows = await context.CommentRelationshipEventQuarantines
            .Where(value => value.Id > job.Cursor
                && (value.OwnerUserId == job.UserId || value.ViewerUserId == job.UserId))
            .OrderBy(value => value.Id).Take(maximumRows).ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return Next(job);
        context.CommentRelationshipEventQuarantines.RemoveRange(rows);
        return new(job.Stage, rows[^1].Id, rows.Count);
    }

    private async Task<StagePage> RemoveActorProjectionsAsync(
        CommentUserErasure job, int maximumRows, CancellationToken cancellationToken)
    {
        var actors = await context.CommentInteractionActorProjections
            .Where(value => value.ActorId == job.UserId)
            .OrderBy(value => value.CommentId).Take(maximumRows).ToListAsync(cancellationToken);
        if (actors.Count > 0)
        {
            context.CommentInteractionActorProjections.RemoveRange(actors);
            return new(job.Stage, job.Cursor, actors.Count);
        }
        return Next(job);
    }

    private async Task<BatchResult> ProcessMediaCleanupAsync(
        CommentUserErasure snapshot,
        string workerId,
        long fencingToken,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var rows = await context.CommentErasureMediaObjects.AsNoTracking()
            .Where(value => value.UserId == snapshot.UserId && value.DeletedAtUtc == null
                && value.Id > snapshot.Cursor)
            .OrderBy(value => value.Id).Take(maximumRows).ToArrayAsync(cancellationToken);
        if (rows.Length == 0)
            return await AdvanceMediaStageAsync(snapshot, workerId, fencingToken, cancellationToken);

        try
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.ObjectKind == "final")
                    await objects.DeleteFinalAsync(row.UserId, row.ObjectName, cancellationToken);
                else
                    await objects.DeleteQuarantineAsync(row.UserId, row.ObjectName, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await RecordMediaFailureAsync(snapshot, workerId, fencingToken,
                rows[0].Id, cancellationToken);
            return BatchResult.StopNow("media_cleanup");
        }

        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var job = await context.CommentUserErasures.FromSqlInterpolated($"""
            SELECT * FROM `CommentUserErasure` WHERE `UserId` = {snapshot.UserId} FOR UPDATE
            """).SingleAsync(cancellationToken);
        var now = UtcNow;
        if (!job.OwnsLease(workerId, fencingToken, snapshot.ErasureVersion, now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BatchResult.Lost;
        }
        var ids = rows.Select(value => value.Id).ToArray();
        var tracked = await context.CommentErasureMediaObjects
            .Where(value => ids.Contains(value.Id)).ToListAsync(cancellationToken);
        foreach (var row in tracked)
            row.MarkDeleted(now);
        job.Advance(job.Stage, rows[^1].Id, workerId, fencingToken,
            snapshot.ErasureVersion, now, _options.LeaseDuration);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(rows.Length, false, false, false, false, "media_cleanup");
    }

    private async Task<BatchResult> AdvanceMediaStageAsync(
        CommentUserErasure snapshot,
        string workerId,
        long fencingToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var job = await context.CommentUserErasures.FromSqlInterpolated($"""
            SELECT * FROM `CommentUserErasure` WHERE `UserId` = {snapshot.UserId} FOR UPDATE
            """).SingleAsync(cancellationToken);
        var now = UtcNow;
        if (!job.OwnsLease(workerId, fencingToken, snapshot.ErasureVersion, now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BatchResult.Lost;
        }
        job.Advance(CommentUserErasureStage.MediaRows, 0, workerId, fencingToken,
            snapshot.ErasureVersion, now, _options.LeaseDuration);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(0, false, false, false, false, "media_cleanup");
    }

    private async Task RecordMediaFailureAsync(
        CommentUserErasure snapshot,
        string workerId,
        long fencingToken,
        long mediaId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var job = await context.CommentUserErasures.FromSqlInterpolated($"""
            SELECT * FROM `CommentUserErasure` WHERE `UserId` = {snapshot.UserId} FOR UPDATE
            """).SingleAsync(cancellationToken);
        var now = UtcNow;
        if (!job.OwnsLease(workerId, fencingToken, snapshot.ErasureVersion, now))
            return;
        var media = await context.CommentErasureMediaObjects
            .SingleAsync(value => value.Id == mediaId, cancellationToken);
        media.RecordFailure("delete_failed", now);
        job.Release(workerId, fencingToken, snapshot.ErasureVersion, now, "media_delete_failed");
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<StagePage> RemoveMediaRowsAsync(
        CommentUserErasure job, int maximumRows, CancellationToken cancellationToken)
    {
        var rows = await context.CommentMediaIngestions.Include(value => value.Items)
            .Where(value => value.OwnerUserId == job.UserId && value.Id > job.Cursor)
            .OrderBy(value => value.Id).Take(maximumRows).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            var completedObjects = await context.CommentErasureMediaObjects
                .Where(value => value.UserId == job.UserId && value.DeletedAtUtc != null)
                .OrderBy(value => value.Id).Take(maximumRows).ToListAsync(cancellationToken);
            if (completedObjects.Count == 0)
                return Next(job);
            context.CommentErasureMediaObjects.RemoveRange(completedObjects);
            return new(job.Stage, job.Cursor, completedObjects.Count);
        }
        var ids = rows.Select(value => value.Id).ToArray();
        if (await context.CommentErasureMediaObjects.AnyAsync(value =>
            value.UserId == job.UserId && ids.Contains(value.IngestionId)
            && value.DeletedAtUtc == null, cancellationToken))
            throw new InvalidOperationException("Media metadata cannot be removed before exact objects are deleted.");
        context.CommentMediaIngestions.RemoveRange(rows);
        return new(job.Stage, rows[^1].Id, rows.Count);
    }

    private async Task ReleaseAsync(
        Claim claim,
        string workerId,
        string reason,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var job = await context.CommentUserErasures
            .SingleAsync(value => value.UserId == claim.UserId, cancellationToken);
        job.Release(workerId, claim.FencingToken, claim.ErasureVersion, UtcNow, reason);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureExactObjectNames(
        CommentMediaIngestion ingestion,
        CommentMediaIngestionItem item)
    {
        var stem = $"{ingestion.UploadIdentity}-{item.Order:D2}";
        if (!string.Equals(item.QuarantineObjectName, stem + ".blob", StringComparison.Ordinal))
            throw new AmbiguousMediaOwnershipException();
        if (item.PromotedAtUtc.HasValue
            && (string.IsNullOrWhiteSpace(item.CanonicalExtension)
                || !string.Equals(item.FinalObjectName, stem + item.CanonicalExtension,
                    StringComparison.Ordinal)))
            throw new AmbiguousMediaOwnershipException();
    }

    private static StagePage Advance<T>(
        CommentUserErasure job,
        IReadOnlyList<T> rows,
        Func<T, long> cursor) => rows.Count == 0
        ? Next(job)
        : new StagePage(job.Stage, cursor(rows[^1]), rows.Count);

    private static StagePage Next(CommentUserErasure job) =>
        new((CommentUserErasureStage)((byte)job.Stage + 1), 0, 0);

    private static void ValidateWorker(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128)
            throw new ArgumentException("A bounded Comment erasure worker id is required.", nameof(workerId));
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record Claim(int UserId, long ErasureVersion, long FencingToken);
    private sealed record StagePage(CommentUserErasureStage NextStage, long NextCursor, int Rows);
    private sealed record BatchResult(
        int Rows,
        bool Completed,
        bool Blocked,
        bool LeaseLost,
        bool Stop,
        string Stage)
    {
        public static BatchResult Lost { get; } = new(0, false, false, true, false, "lease");
        public static BatchResult StopNow(string stage) => new(0, false, false, false, true, stage);
    }

    private sealed class AmbiguousMediaOwnershipException : Exception;
}
