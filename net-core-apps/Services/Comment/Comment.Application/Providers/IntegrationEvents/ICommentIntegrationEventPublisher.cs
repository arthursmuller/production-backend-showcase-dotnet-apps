using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Providers.IntegrationEvents;

public static class CommentFeedActivityStates
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Deleted = "deleted";
}

public sealed record CommentPostCountChangedIntegrationEvent(
    string RelatedEntityId,
    bool Increment,
    int UserId,
    string PostType,
    DateTime OccurredAtUtc);

public sealed record CommentCategoryMentionsChangedIntegrationEvent(
    string[] NamesToAdd,
    string[] NamesToRemove,
    DateTime OccurredAtUtc);

public sealed record CommentFeedActivityIntegrationEvent(
    int CommentId,
    int AuthorUserId,
    string AudiencePolicy,
    string State,
    long StreamVersion,
    long RankKey,
    DateTime OccurredAtUtc,
    string ReasonCode);

public sealed record CommentMentionNotificationIntegrationEvent(
    int RecipientUserId,
    int AuthorUserId,
    int CommentId,
    DateTime OccurredAtUtc);

/// <summary>
/// The only Application boundary for Comment-owned integration publications.
/// Infrastructure maps these intents to canonical broker contracts and stages
/// them through MassTransit's EF bus outbox.
/// </summary>
public interface ICommentIntegrationEventPublisher
{
    Task PublishAsync(
        CommentPostCountChangedIntegrationEvent message,
        CancellationToken cancellationToken);

    Task PublishAsync(
        CommentCategoryMentionsChangedIntegrationEvent message,
        CancellationToken cancellationToken);

    Task PublishAsync(
        CommentFeedActivityIntegrationEvent message,
        CancellationToken cancellationToken);

    Task PublishAsync(
        CommentMentionNotificationIntegrationEvent message,
        CancellationToken cancellationToken);
}
