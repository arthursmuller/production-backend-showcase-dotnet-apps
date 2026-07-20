namespace Interaction.Application.Dtos;

public sealed record EngagementSummaryDto(
    string ResourceType,
    string ResourceId,
    long LikeCount,
    long DislikeCount,
    long RatingCount,
    double? AverageRating,
    long ViewCount,
    long CommentCount,
    long ShareCount,
    long Version);

public sealed record ViewerInteractionStatusDto(
    string ResourceType,
    string ResourceId,
    bool IsAvailable,
    string? Reaction,
    byte? Rating,
    long? ReactionVersion,
    long? RatingVersion);
