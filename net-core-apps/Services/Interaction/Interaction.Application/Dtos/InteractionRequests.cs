using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Dtos;

public sealed record SetReactionDto(ResourceRef Resource, ReactionKind Kind);
public sealed record SetRatingDto(ResourceRef Resource, byte Score, string? Comment);

public sealed record ResourceStateUpdate(
    ResourceRef Resource,
    ResourceLifecycle Lifecycle,
    ResourceVisibility Visibility,
    long SourceVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record ResourceContributionUpdate(
    ResourceRef Resource,
    long CommentCount,
    long ShareCount,
    long SourceVersion,
    DateTimeOffset OccurredAtUtc);
