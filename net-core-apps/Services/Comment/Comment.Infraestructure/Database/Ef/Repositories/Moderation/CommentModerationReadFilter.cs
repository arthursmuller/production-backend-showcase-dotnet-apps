using Comment.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Repositories.Moderation;

internal static class CommentModerationReadFilter
{
    public static IQueryable<CommentPost> ExcludeModerationSuppressed(
        this IQueryable<CommentPost> query,
        CommentContext context) =>
        query.Where(comment =>
            !context.CommentModerationDecisionProjections.Any(decision =>
                decision.CommentId == comment.Id && decision.IsSuppressed));

    public static Task<bool> IsReadableAsync(
        this CommentContext context,
        int commentId,
        CancellationToken cancellationToken) =>
        context.Comments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(comment => comment.Id == commentId)
            .ExcludeModerationSuppressed(context)
            .AnyAsync(cancellationToken);
}
