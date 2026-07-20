namespace Interaction.Contracts;

public enum ResourceLifecycle : byte
{
    Active = 1,
    Deleted = 2,
}

public enum ResourceVisibility : byte
{
    Public = 1,
    Restricted = 2,
    Private = 3,
}

public enum ReactionKind : byte
{
    Like = 1,
    Dislike = 2,
}

public sealed record CommentResourceStateChanged(
    string CommentId,
    ResourceLifecycle Lifecycle,
    ResourceVisibility Visibility,
    long SourceVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record PostResourceStateChanged(
    string PostId,
    ResourceLifecycle Lifecycle,
    ResourceVisibility Visibility,
    long SourceVersion,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Absolute source-owned comment/share counters. Reports are deliberately absent.
/// </summary>
public sealed record ResourceContributionChanged(
    string ResourceType,
    string ResourceId,
    long CommentCount,
    long ShareCount,
    long SourceVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record ReactionChanged(
    Guid EventId,
    string ResourceType,
    string ResourceId,
    long ActorId,
    ReactionKind? Kind,
    long InteractionVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record RatingChanged(
    Guid EventId,
    string ResourceType,
    string ResourceId,
    long ActorId,
    byte? Score,
    long InteractionVersion,
    DateTimeOffset OccurredAtUtc);

/// <summary>Public projection contract. Moderation/report data must never be added.</summary>
public sealed record EngagementSummaryChanged(
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
    DateTimeOffset OccurredAtUtc);
