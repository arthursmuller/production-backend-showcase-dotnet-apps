using Comment.Domain.Repositories;
using Comment.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Comment.Infra.Persistence.Repositories.Moderation;
using Comment.Infra.Persistence.Repositories.Media;

namespace Comment.Infra.Persistence.Repositories
{
    public sealed class CommentRepository(CommentContext context) : ICommentRepository
    {
        private readonly CommentContext _context = context;

        public Task<CommentPost?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            AggregateQuery().FirstOrDefaultAsync(comment => comment.Id == id, cancellationToken);

        public async Task<CommentPost> AddAsync(
            CommentPost comment,
            CancellationToken cancellationToken)
        {
            await using var transaction = _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                _context.Comments.Add(comment);
                await _context.SaveChangesAsync(cancellationToken);

                await _context.SaveEntitiesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return comment;
            }
            catch
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task RemoveAsync(CommentPost comment, CancellationToken cancellationToken)
        {
            await using var transaction = _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                await CommentMediaDeletionScheduler.StageAsync(
                    _context,
                    comment,
                    comment.MediaItems,
                    cancellationToken);
                _context.Comments.Remove(comment);
                await _context.SaveEntitiesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<CommentPost> SaveUpdatedAsync(
            CommentPost comment,
            CancellationToken cancellationToken)
        {
            await using var transaction = _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                _context.ChangeTracker.DetectChanges();
                var removedMedia = _context.ChangeTracker
                    .Entries<CommentMediaItem>()
                    .Where(entry => entry.State == EntityState.Deleted)
                    .Select(entry => entry.Entity)
                    .ToArray();
                await CommentMediaDeletionScheduler.StageAsync(
                    _context,
                    comment,
                    removedMedia,
                    cancellationToken);

                var reorderedMedia = _context.ChangeTracker
                    .Entries<CommentMediaItem>()
                    .Where(entry =>
                        entry.State == EntityState.Modified &&
                        entry.Property(nameof(CommentMediaItem.Order)).IsModified)
                    .Select(entry => new
                    {
                        Entry = entry,
                        FinalOrder = entry.Entity.Order,
                    })
                    .ToList();

                // MariaDB checks unique indexes row-by-row. Move existing rows to
                // collision-free temporary values before applying a swap such as
                // order 0 <-> 1, all inside this transaction.
                if (reorderedMedia.Count > 0)
                {
                    foreach (var media in reorderedMedia)
                        media.Entry.Property(nameof(CommentMediaItem.Order)).CurrentValue =
                            -media.Entry.Entity.Id - 1;

                    await _context.SaveChangesAsync(cancellationToken);

                    foreach (var media in reorderedMedia)
                        media.Entry.Property(nameof(CommentMediaItem.Order)).CurrentValue =
                            media.FinalOrder;
                }

                await _context.SaveEntitiesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return comment;
            }
            catch
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private IQueryable<CommentPost> AggregateQuery() =>
            _context.Comments
                .IgnoreQueryFilters()
                .ExcludeModerationSuppressed(_context)
                .Include(comment => comment.MediaItems)
                .Include(comment => comment.Categories)
                .Include(comment => comment.Mentions);
    }
}
