using System;

namespace Comment.Domain.AggregatesModel.Interactions;

/// <summary>
/// Durable out-of-order summary value retained until preceding versions arrive.
/// </summary>
public sealed class CommentInteractionSummaryGap
{
    private CommentInteractionSummaryGap() { }

    public CommentInteractionSummaryGap(
        int commentId,
        CommentInteractionSummarySnapshot snapshot)
    {
        if (commentId <= 0 || snapshot.Version <= 0)
            throw new InvalidOperationException("Interaction summary gap coordinates are invalid.");

        CommentId = commentId;
        SummaryVersion = snapshot.Version;
        LikeCount = snapshot.LikeCount;
        DislikeCount = snapshot.DislikeCount;
        RatingCount = snapshot.RatingCount;
        RatingTotal = snapshot.RatingTotal;
        ViewCount = snapshot.ViewCount;
        CommentCount = snapshot.CommentCount;
        ShareCount = snapshot.ShareCount;
        OccurredAtUtc = snapshot.OccurredAtUtc;
    }

    public int CommentId { get; private set; }
    public long SummaryVersion { get; private set; }
    public long LikeCount { get; private set; }
    public long DislikeCount { get; private set; }
    public long RatingCount { get; private set; }
    public long RatingTotal { get; private set; }
    public long ViewCount { get; private set; }
    public long CommentCount { get; private set; }
    public long ShareCount { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public CommentInteractionSummarySnapshot ToSnapshot() => new(
        LikeCount,
        DislikeCount,
        RatingCount,
        RatingTotal,
        ViewCount,
        CommentCount,
        ShareCount,
        SummaryVersion,
        OccurredAtUtc);
}
