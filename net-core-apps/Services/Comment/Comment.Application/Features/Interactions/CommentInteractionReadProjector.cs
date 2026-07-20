using Comment.Application.Dtos;
using Comment.Application.Dtos.Interactions;
using Comment.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Interactions;

/// <summary>
/// Applies authoritative counters to an already bounded Comment query result.
/// Missing or gapped projections fail the complete result.
/// </summary>
public sealed class CommentInteractionReadProjector(
    ICommentInteractionProjectionStore projections)
{
    public Task ApplyToCursorDtosAsync(
        IReadOnlyCollection<CommentCursorItemDto> comments,
        CancellationToken cancellationToken) => ApplyAsync(
            comments,
            comment => comment.Id,
            (comment, summary) => Apply(comment.Engagement, summary),
            cancellationToken);

    private async Task ApplyAsync<T>(
        IReadOnlyCollection<T> comments,
        Func<T, int> getId,
        Action<T, CommentInteractionSummary> apply,
        CancellationToken cancellationToken)
    {
        if (comments is null || comments.Count == 0)
            return;

        var ids = comments.Select(getId).Distinct().ToArray();
        var summaries = await projections.GetSummariesAsync(ids, cancellationToken);
        if (ids.Any(id =>
            !summaries.TryGetValue(id, out var summary) || summary.HasGap))
        {
            throw new CommentException(
                "The authoritative Interaction summary projection is temporarily unavailable.",
                503);
        }

        // Validate the complete page before mutating the HTTP DTO graph.
        foreach (var comment in comments)
            apply(comment, summaries[getId(comment)]);
    }

    private static void Apply(
        CommentCursorEngagementDto engagement,
        CommentInteractionSummary summary)
    {
        if (engagement is null)
        {
            throw new CommentException(
                "The Comment engagement read model is unavailable.",
                503);
        }

        if (summary.Version <= 0 ||
            summary.ViewCount < 0 || summary.RatingCount < 0 ||
            summary.RatingTotal < 0 || summary.LikeCount < 0 ||
            summary.DislikeCount < 0 || summary.ShareCount < 0 ||
            summary.CommentCount < 0)
        {
            throw new CommentException(
                "Interaction projection counters cannot be negative.");
        }

        engagement.TotalViews = BoundedCount(summary.ViewCount);
        engagement.TotalRatings = BoundedCount(summary.RatingCount);
        engagement.RatingsTrueCount = summary.RatingTotal;
        engagement.TotalLikes = BoundedCount(summary.LikeCount);
        engagement.TotalDislikes = BoundedCount(summary.DislikeCount);
        engagement.TotalShares = BoundedCount(summary.ShareCount);
        engagement.TotalComments = BoundedCount(summary.CommentCount);
        engagement.Version = summary.Version;
    }

    private static int BoundedCount(long value) =>
        value >= int.MaxValue ? int.MaxValue : checked((int)value);
}
