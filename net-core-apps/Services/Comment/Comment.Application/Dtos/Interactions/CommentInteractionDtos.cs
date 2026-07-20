namespace Comment.Application.Dtos.Interactions;

public sealed record CommentInteractionSummary(
    long LikeCount,
    long DislikeCount,
    long RatingCount,
    long RatingTotal,
    long ViewCount,
    long CommentCount,
    long ShareCount,
    long Version,
    bool HasGap);
