using Comment.Application.Features.Interactions;
using Comment.Application.Providers.IntegrationEvents;
using Comment.Infra.Persistence;
using Comment.Infra.Providers;
using MassTransit;
using Comment.Application;
using MessageBrokerContracts.Categories;
using MessageBrokerContracts.Feed;
using MessageBrokerContracts.Notification;
using MessageBrokerContracts.Posts;
using MessageBrokerExtensions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.IntegrationEvents;

public sealed class MassTransitCommentIntegrationEventPublisher(
    IPublishEndpoint publisher,
    CommentContext context) : ICommentIntegrationEventPublisher
{
    private const string CommentActivityKind = "created";
    private const int PlatformNotificationChannelId = 1;

    public Task PublishAsync(
        CommentPostCountChangedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var messageId = Guid.NewGuid();
        return StageAsync(new PostCommentCountChanged
        {
            EventId = messageId,
            OccurredAtUtc = Utc(message.OccurredAtUtc),
            PostId = message.RelatedEntityId,
            PostType = message.PostType,
            ActorUserId = message.UserId,
            Delta = message.Increment ? 1 : -1,
        }, messageId, cancellationToken);
    }

    public Task PublishAsync(
        CommentCategoryMentionsChangedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.NamesToAdd.Length == 0 && message.NamesToRemove.Length == 0)
            return Task.CompletedTask;

        var messageId = Guid.NewGuid();
        return StageAsync(new CategoryMentionsChanged
        {
            EventId = messageId,
            OccurredAtUtc = Utc(message.OccurredAtUtc),
            NamesAdded = message.NamesToAdd,
            NamesRemoved = message.NamesToRemove,
        }, messageId, cancellationToken);
    }

    public Task PublishAsync(
        CommentFeedActivityIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.CommentId <= 0 || message.AuthorUserId <= 0 ||
            message.StreamVersion <= 0)
            throw new InvalidOperationException("Comment feed activity coordinates are invalid.");

        var messageId = Guid.NewGuid();
        var targetId = message.CommentId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return StageAsync(new FeedActivityChanged
        {
            EventId = messageId,
            StreamId = CommentInteractionMessageIdentity.Create(
                "comment-feed-activity",
                targetId,
                CommentActivityKind),
            StreamVersion = message.StreamVersion,
            TargetType = CommentPlatform.EntityType,
            TargetId = targetId,
            ActivityKind = CommentActivityKind,
            ActorUserId = message.AuthorUserId,
            AudiencePolicy = FeedAudiencePolicies.NormalizeOrOwner(
                message.AudiencePolicy),
            State = message.State,
            RankKey = message.RankKey,
            PublishedAtUtc = Utc(message.OccurredAtUtc),
            OccurredAtUtc = Utc(message.OccurredAtUtc),
            ReasonCode = message.ReasonCode,
        }, messageId, cancellationToken);
    }

    public Task PublishAsync(
        CommentMentionNotificationIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var messageId = Guid.NewGuid();
        return StageAsync(new NotificationRequested
        {
            EventId = messageId,
            OccurredAtUtc = Utc(message.OccurredAtUtc),
            RecipientUserId = message.RecipientUserId,
            SourceUserId = message.AuthorUserId,
            ReplacementUserId = message.AuthorUserId,
            SourceEntityType = CommentPlatform.EntityType,
            SourceEntityId = message.CommentId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Title = "Você foi mencionado!",
            Message = "{username} mencionou você em um comentário",
            ChannelIds = [PlatformNotificationChannelId],
        }, messageId, cancellationToken);
    }

    private async Task StageAsync<T>(
        T message,
        Guid messageId,
        CancellationToken cancellationToken)
        where T : class
    {
        await context.PublishToBusOutboxAsync(
            publisher,
            message,
            messageId,
            cancellationToken);
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
