using Comment.Application.Providers.IntegrationEvents;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Events;
using Mediator;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.EventHandlers.Comments
{
    public sealed class CommentFeedRemoveRequestedEventHandler(
        ICommentIntegrationEventPublisher publisher)
        : INotificationHandler<CommentFeedRemoveRequestedEvent>
    {
        public async ValueTask Handle(
            CommentFeedRemoveRequestedEvent notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await publisher.PublishAsync(
                new CommentFeedActivityIntegrationEvent(
                    notification.CommentId,
                    notification.AuthorUserId,
                    CommentAudiencePolicies.Owner,
                    CommentFeedActivityStates.Deleted,
                    notification.SourceVersion,
                    notification.OccurredAtUtc.Ticks,
                    notification.OccurredAtUtc,
                    "comment-deleted"),
                cancellationToken);
        }
    }
}
