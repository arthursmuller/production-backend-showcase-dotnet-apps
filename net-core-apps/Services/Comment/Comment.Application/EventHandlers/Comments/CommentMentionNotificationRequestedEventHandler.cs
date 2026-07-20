using Comment.Application.Providers.IntegrationEvents;
using Comment.Domain.Events;
using Mediator;
using System.Threading;
using System.Threading.Tasks;
using Comment.Application.Features.Visibility;
using System;

namespace Comment.Application.EventHandlers.Comments
{
    public sealed class CommentMentionNotificationRequestedEventHandler(
        ICommentIntegrationEventPublisher publisher,
        TimeProvider timeProvider,
        ICommentAudienceAuthorization? audience = null)
        : INotificationHandler<CommentMentionNotificationRequestedEvent>
    {
        public async ValueTask Handle(
            CommentMentionNotificationRequestedEvent notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comment = notification.Comment;
            // Restricted identifiers must never escape through notification payloads. A missing,
            // stale, disabled, or timed-out projection suppresses the side effect fail closed.
            if (audience is null)
                return;
            var decision = await audience.EvaluateAudienceAsync(
                comment.User.Id,
                notification.AudiencePolicy,
                notification.RecipientUserId,
                cancellationToken);
            if (!decision.Allowed)
                return;
            await publisher.PublishAsync(
                new CommentMentionNotificationIntegrationEvent(
                    notification.RecipientUserId,
                    comment.User.Id,
                    comment.Id,
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken);
        }
    }
}
