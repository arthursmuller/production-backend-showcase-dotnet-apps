using Comment.Domain.AggregatesModel;
using Comment.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Repositories
{
    /// <summary>
    /// Value-only SQL projections for the high-volume comment list. The root
    /// keyset query selects at most pageSize + 1 rows; collection queries are then
    /// restricted to the selected page IDs so no aggregate graph is hydrated.
    /// </summary>
    internal static class CommentCursorQueries
    {
        internal static async Task<CommentCursorSlice> PageAsync(
            IQueryable<CommentPost> source,
            CommentCursorPosition? after,
            int pageSize,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (pageSize is < 1 or > CommentReadLimits.MaximumCursorPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (after is { Id: <= 0 })
                throw new ArgumentOutOfRangeException(nameof(after));

            var pageQuery = source;
            if (after is not null)
            {
                pageQuery = pageQuery.Where(comment =>
                    comment.CreatedDate < after.CreatedDateUtc ||
                    (comment.CreatedDate == after.CreatedDateUtc && comment.Id < after.Id));
            }

            var rows = await pageQuery
                .OrderByDescending(comment => comment.CreatedDate)
                .ThenByDescending(comment => comment.Id)
                .Take(pageSize + 1)
                .Select(comment => new CommentCursorReadModel(
                    comment.Id,
                    comment.CreatedDate,
                    comment.Description,
                    comment.GifUrl,
                    comment.TargetId,
                    comment.TargetType,
                    comment.User.Id,
                    comment.User.FullName,
                    comment.VisibilityLevelId,
                    comment.Categories
                        .OrderBy(category => category.Name)
                        .Select(category => new CommentCategoryCursorReadModel(
                            category.Name))
                        .ToList(),
                    comment.MediaItems
                        .OrderBy(media => media.Order)
                        .ThenBy(media => media.Id)
                        .Select(media => new CommentMediaCursorReadModel(
                            media.Id,
                            media.Order,
                            media.Url,
                            media.FileName,
                            media.MediaType,
                            media.ThumbnailUrl,
                            media.DurationSeconds,
                            media.MimeType,
                            media.CreatedDate))
                        .ToList(),
                    comment.Mentions
                        .OrderBy(mention => mention.UserId)
                        .Select(mention => new CommentMentionCursorReadModel(
                            mention.UserId,
                            mention.UserName))
                        .ToList()))
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            var hasMore = rows.Count > pageSize;
            if (hasMore)
                rows.RemoveAt(pageSize);

            return new CommentCursorSlice(rows, hasMore);
        }
    }
}
