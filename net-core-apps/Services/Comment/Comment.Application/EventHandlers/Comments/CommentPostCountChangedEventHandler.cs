using Comment.Application.Providers.IntegrationEvents;
using Comment.Domain.Events;
using Mediator;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.EventHandlers.Comments
{
    public sealed class CommentPostCountChangedEventHandler(
        ICommentIntegrationEventPublisher publisher,
        TimeProvider timeProvider)
        : INotificationHandler<CommentPostCountChangedEvent>
    {
        public async ValueTask Handle(
            CommentPostCountChangedEvent notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await publisher.PublishAsync(
                new CommentPostCountChangedIntegrationEvent(
                    notification.RelatedEntityId,
                    notification.Increment,
                    notification.UserId,
                    notification.PostType,
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken);
        }
    }
}
