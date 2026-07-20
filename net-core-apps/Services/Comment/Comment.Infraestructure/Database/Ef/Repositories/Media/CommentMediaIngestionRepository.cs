using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Repositories.Media;

public sealed class CommentMediaIngestionRepository(CommentContext context)
    : ICommentMediaIngestionRepository
{
    public async Task<CommentMediaIngestionGetResult> GetOrCreateAsync(
        CommentMediaIngestion candidate,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(
            candidate.UploadIdentity,
            candidate.OwnerUserId,
            cancellationToken);
        if (existing is not null)
            return new(existing, Created: false);

        context.CommentMediaIngestions.Add(candidate);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(candidate, Created: true);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in context.ChangeTracker
                .Entries<CommentMediaIngestionItem>()
                .Where(entry => candidate.Items.Contains(entry.Entity))
                .ToList())
                entry.State = EntityState.Detached;
            context.Entry(candidate).State = EntityState.Detached;
            existing = await FindAsync(
                candidate.UploadIdentity,
                candidate.OwnerUserId,
                cancellationToken);
            if (existing is null)
                throw;
            return new(existing, Created: false);
        }
    }

    public Task<CommentMediaIngestion?> FindAsync(
        string uploadIdentity,
        int ownerUserId,
        CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(value =>
            value.UploadIdentity == uploadIdentity
            && value.OwnerUserId == ownerUserId,
            cancellationToken);

    public async Task SaveAsync(
        CommentMediaIngestion ingestion,
        CancellationToken cancellationToken)
    {
        if (context.Entry(ingestion).State == EntityState.Detached)
            context.CommentMediaIngestions.Update(ingestion);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryClaimForRequestAsync(
        CommentMediaIngestion ingestion,
        string leaseOwner,
        DateTime utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (!ingestion.TryClaim(
            leaseOwner,
            utcNow,
            leaseDuration,
            countAttempt: false))
            return false;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public Task<bool> RecordBindingFailureAsync(
        string uploadIdentity,
        int ownerUserId,
        DateTime utcNow,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken) =>
        RecordBindingFailureCoreAsync(
            uploadIdentity,
            ownerUserId,
            utcNow,
            nextAttemptAtUtc,
            cancellationToken);

    private async Task<bool> RecordBindingFailureCoreAsync(
        string uploadIdentity,
        int ownerUserId,
        DateTime utcNow,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdate deliberately bypasses the change tracker. A failed
        // binding transaction may leave unrelated tracked entries dirty;
        // flushing them here would turn a rollback into an out-of-transaction
        // business write.
        var affected = await context.CommentMediaIngestions
            .Where(value =>
                value.UploadIdentity == uploadIdentity
                && value.OwnerUserId == ownerUserId
                && value.State == CommentMediaIngestionState.Promoted)
            .ExecuteUpdateAsync(update => update
                .SetProperty(value => value.LastFailureCode, "binding_failed")
                .SetProperty(value => value.NextAttemptAtUtc, nextAttemptAtUtc)
                .SetProperty(value => value.LeaseOwner, (string?)null)
                .SetProperty(value => value.LeaseUntilUtc, (DateTime?)null)
                .SetProperty(value => value.UpdateDate, utcNow)
                .SetProperty(value => value.Version, value => value.Version + 1),
                cancellationToken);
        return affected == 1;
    }

    public async Task<CommentPost> BindNewCommentAsync(
        CommentMediaIngestion ingestion,
        CommentPost comment,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            context.Comments.Add(comment);
            await context.SaveChangesAsync(cancellationToken);
            ingestion.MarkBound(comment.Id, DateTime.UtcNow);
            await context.SaveEntitiesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return comment;
        }
        catch
        {
            if (transaction is not null)
                await RollbackBestEffortAsync(transaction);
            throw;
        }
    }

    public async Task<CommentPost> BindUpdatedCommentAsync(
        CommentMediaIngestion ingestion,
        CommentPost comment,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            context.ChangeTracker.DetectChanges();
            var removedMedia = context.ChangeTracker
                .Entries<CommentMediaItem>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity)
                .ToArray();
            await CommentMediaDeletionScheduler.StageAsync(
                context,
                comment,
                removedMedia,
                cancellationToken);

            var reorderedMedia = context.ChangeTracker
                .Entries<CommentMediaItem>()
                .Where(entry =>
                    entry.State == EntityState.Modified
                    && entry.Property(nameof(CommentMediaItem.Order)).IsModified)
                .Select(entry => new
                {
                    Entry = entry,
                    FinalOrder = entry.Entity.Order,
                })
                .ToList();
            if (reorderedMedia.Count > 0)
            {
                foreach (var media in reorderedMedia)
                    media.Entry.Property(nameof(CommentMediaItem.Order)).CurrentValue =
                        -media.Entry.Entity.Id - 1;
                await context.SaveChangesAsync(cancellationToken);
                foreach (var media in reorderedMedia)
                    media.Entry.Property(nameof(CommentMediaItem.Order)).CurrentValue = media.FinalOrder;
            }

            ingestion.MarkBound(comment.Id, DateTime.UtcNow);
            await context.SaveEntitiesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return comment;
        }
        catch
        {
            if (transaction is not null)
                await RollbackBestEffortAsync(transaction);
            throw;
        }
    }

    public async Task<IReadOnlyList<CommentMediaIngestion>> ClaimDueAsync(
        string leaseOwner,
        DateTime utcNow,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var ids = await context.CommentMediaIngestions
            .AsNoTracking()
            .Where(value =>
                ((value.State != CommentMediaIngestionState.Bound
                    && value.State != CommentMediaIngestionState.Rejected
                    && value.State != CommentMediaIngestionState.Cleaned)
                    || (value.State == CommentMediaIngestionState.Bound
                        && value.Items.Any(item =>
                            item.State == CommentMediaObjectState.CleanupPending
                            || (item.QuarantinedAtUtc != null
                                && item.QuarantineDeletedAtUtc == null))))
                && value.NextAttemptAtUtc != null
                && value.NextAttemptAtUtc <= utcNow
                && !context.CommentUserErasures.Any(erasure =>
                    erasure.UserId == value.OwnerUserId)
                && (value.LeaseUntilUtc == null || value.LeaseUntilUtc <= utcNow))
            .OrderBy(value => value.NextAttemptAtUtc)
            .ThenBy(value => value.Id)
            .Select(value => value.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var claimed = new List<CommentMediaIngestion>(ids.Count);
        foreach (var id in ids)
        {
            var updated = await context.CommentMediaIngestions
                .Where(value =>
                    value.Id == id
                    && ((value.State != CommentMediaIngestionState.Bound
                        && value.State != CommentMediaIngestionState.Rejected
                        && value.State != CommentMediaIngestionState.Cleaned)
                        || (value.State == CommentMediaIngestionState.Bound
                            && value.Items.Any(item =>
                                item.State == CommentMediaObjectState.CleanupPending
                                || (item.QuarantinedAtUtc != null
                                    && item.QuarantineDeletedAtUtc == null))))
                    && value.NextAttemptAtUtc != null
                    && value.NextAttemptAtUtc <= utcNow
                    && !context.CommentUserErasures.Any(erasure =>
                        erasure.UserId == value.OwnerUserId)
                    && (value.LeaseUntilUtc == null || value.LeaseUntilUtc <= utcNow))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(value => value.LeaseOwner, leaseOwner)
                    .SetProperty(value => value.ClaimedAtUtc, utcNow)
                    .SetProperty(value => value.LeaseUntilUtc, utcNow.Add(leaseDuration))
                    .SetProperty(value => value.Attempts, value => value.Attempts + 1)
                    .SetProperty(value => value.UpdateDate, utcNow)
                    .SetProperty(value => value.Version, value => value.Version + 1),
                    cancellationToken);
            if (updated != 1)
                continue;
            var ingestion = await Query().SingleAsync(value => value.Id == id, cancellationToken);
            claimed.Add(ingestion);
        }
        return claimed;
    }

    public async Task<int> PurgeTerminalAsync(
        DateTime completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var ids = await context.CommentMediaIngestions
            .AsNoTracking()
            .Where(value =>
                (value.State == CommentMediaIngestionState.Rejected
                    || value.State == CommentMediaIngestionState.Cleaned)
                && value.CompletedAtUtc != null
                && value.CompletedAtUtc < completedBeforeUtc
                && !value.Items.Any(item =>
                    item.QuarantinedAtUtc != null
                    && item.QuarantineDeletedAtUtc == null))
            .OrderBy(value => value.CompletedAtUtc)
            .ThenBy(value => value.Id)
            .Select(value => value.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return 0;
        return await context.CommentMediaIngestions
            .Where(value => ids.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<CommentMediaIngestion> Query() =>
        context.CommentMediaIngestions.Include(value => value.Items);

    private static async Task RollbackBestEffortAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await transaction.RollbackAsync(timeout.Token);
        }
        catch
        {
            // Preserve the original failure. Async disposal closes the transaction
            // even when the database cannot acknowledge rollback in the bound.
        }
    }
}
