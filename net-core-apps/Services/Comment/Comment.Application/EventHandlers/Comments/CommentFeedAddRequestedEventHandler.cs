using Comment.Application.Providers.IntegrationEvents;
using Comment.Domain.Events;
using Mediator;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.EventHandlers.Comments
{
    public sealed class CommentFeedAddRequestedEventHandler(
        ICommentIntegrationEventPublisher publisher)
        : INotificationHandler<CommentFeedAddRequestedEvent>
    {
        public async ValueTask Handle(
            CommentFeedAddRequestedEvent notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comment = notification.Comment;
            await publisher.PublishAsync(
                new CommentFeedActivityIntegrationEvent(
                    comment.Id,
                    comment.AuthorUserId,
                    comment.GetAudiencePolicy(),
                    CommentFeedActivityStates.Active,
                    notification.SourceVersion,
                    notification.OccurredAtUtc.Ticks,
                    notification.OccurredAtUtc,
                    "comment-published"),
                cancellationToken);
        }
    }
}
