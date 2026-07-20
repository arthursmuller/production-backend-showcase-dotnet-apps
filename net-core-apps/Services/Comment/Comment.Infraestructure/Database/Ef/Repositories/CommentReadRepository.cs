using Comment.Domain.Repositories;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Interactions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Comment.Infra.Persistence.Visibility;
using Comment.Infra.Persistence.Repositories.Moderation;
using IdentityProvider;

namespace Comment.Infra.Persistence.Repositories
{
    public sealed class CommentReadRepository(
        CommentContext context,
        IIdentityService? identity = null,
        CommentAudienceSqlPolicy? audience = null) : ICommentReadRepository
    {
        public async Task<CommentPost?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            await ReadQuery((await AudienceQueryAsync(cancellationToken))
                .ExcludeModerationSuppressed(context)).FirstOrDefaultAsync(
                comment => comment.Id == id,
                cancellationToken);

        public async Task<CommentCursorSlice> ListCursorAsync(
            CommentQueryCriteria criteria,
            CommentCursorPosition? after,
            int pageSize,
            CancellationToken cancellationToken) =>
            await CursorPageAsync(
                (await AudienceQueryAsync(cancellationToken))
                    .ExcludeModerationSuppressed(context)
                    .AsNoTracking(),
                criteria,
                after,
                pageSize,
                cancellationToken);

        private Task<IQueryable<CommentPost>> AudienceQueryAsync(CancellationToken cancellationToken) =>
            audience is null
                ? Task.FromResult(context.Comments.AsQueryable())
                : audience.ApplyReadFilterAsync(
                    context.Comments,
                    identity?.User?.Id,
                    cancellationToken);

        public Task<CommentCursorSlice> ListOwnedCursorAsync(
            CommentQueryCriteria criteria,
            CommentCursorPosition? after,
            int pageSize,
            int ownerUserId,
            CancellationToken cancellationToken) =>
            CursorPageAsync(
                context.Comments
                    .IgnoreQueryFilters()
                    .Where(comment => comment.AuthorUserId == ownerUserId)
                    .ExcludeModerationSuppressed(context)
                    .AsNoTracking(),
                criteria,
                after,
                pageSize,
                cancellationToken);

        public async Task<IReadOnlyList<CommentInteractionStatus>> GetInteractionStatusesAsync(
            int[] commentIds,
            int userId,
            CancellationToken cancellationToken)
        {
            if (commentIds is null || commentIds.Length == 0 || userId <= 0)
                return Array.Empty<CommentInteractionStatus>();

            var suppressedIds = await context.CommentModerationDecisionProjections
                .AsNoTracking()
                .Where(decision =>
                    commentIds.Contains(decision.CommentId) && decision.IsSuppressed)
                .Select(decision => decision.CommentId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var readableIds = commentIds.Except(suppressedIds).ToArray();
            if (readableIds.Length == 0)
                return Array.Empty<CommentInteractionStatus>();

            var results = await context.CommentInteractionActorProjections
                .AsNoTracking()
                .Where(item =>
                    item.ActorId == userId &&
                    readableIds.Contains(item.CommentId))
                .Select(item => new
                {
                    item.CommentId,
                    item.Reaction,
                    HasRating = item.Rating.HasValue,
                })
                .ToListAsync(cancellationToken);

            return readableIds.Select(commentId =>
            {
                var status = results.SingleOrDefault(result => result.CommentId == commentId);
                return new CommentInteractionStatus(
                    commentId,
                    status?.Reaction == CommentReactionKind.Like,
                    status?.Reaction == CommentReactionKind.Dislike,
                    status?.HasRating == true);
            }).ToList();
        }

        private static Task<CommentCursorSlice> CursorPageAsync(
            IQueryable<CommentPost> query,
            CommentQueryCriteria criteria,
            CommentCursorPosition? after,
            int pageSize,
            CancellationToken cancellationToken)
        {
            criteria ??= new CommentQueryCriteria();
            query = ApplyFilters(query, criteria);
            return CommentCursorQueries.PageAsync(
                query,
                after,
                pageSize,
                cancellationToken);
        }

        private static IQueryable<CommentPost> ApplyFilters(
            IQueryable<CommentPost> query,
            CommentQueryCriteria criteria)
        {
            if (criteria.Targets is { Length: > 0 })
                query = query.Where(BuildTargetPredicate(criteria.Targets));
            if (criteria.UserId.HasValue)
                query = query.Where(comment => comment.AuthorUserId == criteria.UserId.Value);
            if (criteria.Ids is { Length: > 0 })
                query = query.Where(comment => criteria.Ids.Contains(comment.Id));
            var start = criteria.DateRange is { Length: > 0 }
                ? criteria.DateRange[0]
                : null;
            if (start.HasValue)
            {
                var startValue = start.Value;
                query = query.Where(comment => comment.CreatedDate >= startValue);
            }
            var end = criteria.DateRange is { Length: > 1 }
                ? criteria.DateRange[1]
                : null;
            if (end.HasValue)
            {
                var endValue = end.Value;
                query = query.Where(comment => comment.CreatedDate <= endValue);
            }
            return query;
        }

        private static IQueryable<CommentPost> ReadQuery(IQueryable<CommentPost> query) =>
            query
                .Include(comment => comment.MediaItems)
                .Include(comment => comment.Categories)
                .Include(comment => comment.Mentions)
                .AsSplitQuery()
                .AsNoTracking();

        private static Expression<Func<CommentPost, bool>> BuildTargetPredicate(
            IEnumerable<CommentTarget> targets)
        {
            var comment = Expression.Parameter(typeof(CommentPost), "comment");
            Expression body = Expression.Constant(false);

            foreach (var target in targets
                .Where(target =>
                    target is not null &&
                    !string.IsNullOrWhiteSpace(target.TargetId) &&
                    !string.IsNullOrWhiteSpace(target.TargetType))
                .Take(100))
            {
                var id = Expression.Equal(
                    Expression.Property(comment, nameof(CommentPost.TargetId)),
                    Expression.Constant(target.TargetId.Trim()));
                var type = Expression.Equal(
                    Expression.Property(comment, nameof(CommentPost.TargetType)),
                    Expression.Constant(target.TargetType.Trim().ToLowerInvariant()));
                body = Expression.OrElse(body, Expression.AndAlso(type, id));
            }

            return Expression.Lambda<Func<CommentPost, bool>>(body, comment);
        }

    }
}
