using Comment.Application.Providers.IntegrationEvents;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Events;
using Mediator;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.EventHandlers.Comments
{
    public sealed class CommentFeedVisibilityChangedEventHandler(
        ICommentIntegrationEventPublisher publisher)
        : INotificationHandler<CommentFeedVisibilityChangedEvent>
    {
        public async ValueTask Handle(
            CommentFeedVisibilityChangedEvent notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = string.Equals(
                notification.AudiencePolicy,
                CommentAudiencePolicies.Owner,
                System.StringComparison.Ordinal)
                ? CommentFeedActivityStates.Revoked
                : CommentFeedActivityStates.Active;
            await publisher.PublishAsync(
                new CommentFeedActivityIntegrationEvent(
                    notification.CommentId,
                    notification.AuthorUserId,
                    notification.AudiencePolicy,
                    state,
                    notification.SourceVersion,
                    notification.OccurredAtUtc.Ticks,
                    notification.OccurredAtUtc,
                    state == CommentFeedActivityStates.Revoked
                        ? "comment-owner-only"
                        : "comment-audience-changed"),
                cancellationToken);
        }
    }
}
