using System;

namespace Feed.Application.Dtos
{
    public sealed record EngagementSummaryDto(
        long LikeCount,
        long DislikeCount,
        long RatingCount,
        long RatingTotal,
        long ViewCount,
        long CommentCount,
        long ShareCount,
        long SummaryVersion,
        DateTime SourceOccurredAtUtc);

    public sealed record EngagementSummaryProjectionDto(
        Guid EventId,
        string ResourceType,
        string ResourceId,
        long LikeCount,
        long DislikeCount,
        long RatingCount,
        long RatingTotal,
        long ViewCount,
        long CommentCount,
        long ShareCount,
        long SummaryVersion,
        DateTime SourceOccurredAtUtc);
}
