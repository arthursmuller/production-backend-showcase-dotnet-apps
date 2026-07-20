using Comment.Application.Providers.IntegrationEvents;
using Comment.Domain.Events;
using Mediator;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.EventHandlers.Comments
{
    public sealed class CommentCategoryMentionsChangedEventHandler(
        ICommentIntegrationEventPublisher publisher,
        TimeProvider timeProvider)
        : INotificationHandler<CommentCategoryMentionsChangedEvent>
    {
        public async ValueTask Handle(
            CommentCategoryMentionsChangedEvent notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await publisher.PublishAsync(
                new CommentCategoryMentionsChangedIntegrationEvent(
                    notification.NamesToAdd,
                    notification.NamesToRemove,
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken);
        }
    }
}
