using CategoriesProvider.Abstractions;
using Comment.Application.Providers.Notifications;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Events;
using FeedProvider.Abstractions;
using IdentityProvider;
using Mediator;
using StarterEntities;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.EventHandlers.Comment
{
    public sealed class CommentUpdatedEventHandler(
        IFeedService _feed,
        ICategoriesService _categories,
        IIdentityService _auth,
        INotifications _notifications) : INotificationHandler<CommentUpdatedEvent>
    {
        public ValueTask Handle(CommentUpdatedEvent notification, CancellationToken cancellationToken)
        {
            var entity = notification.Entity;
            var previousVisibilityLevel = notification.PreviousVisibilityLevel;
            var userId = _auth.User.Id;

            if (previousVisibilityLevel != entity.VisibilityLevel)
            {
                if (entity.PublishedOnFeed)
                {
                    _feed.Broker.ChangeVisibility(
                        PlatformContexts.Comment.Name,
                        $"{entity.Id}",
                        entity.VisibilityLevel.IsVisible());
                }
                else if (entity.VisibilityLevel.IsVisible())
                {
                    _categories.Broker.UpdateMentions(
                        new(entity.Categories?.Select(e => e.Name)?.ToArray()));

                    _feed.Broker.Add(new(
                        userId,
                        "",
                        "Created",
                        $"{entity.Id}",
                        userId,
                        PlatformContexts.Comment.Name,
                        entity.CreatedDate));

                    entity.PublishedOnFeed = true;
                }
            }

            NotifyMentions(entity, userId);
            return ValueTask.CompletedTask;
        }

        private void NotifyMentions(CommentPost entity, int authorUserId)
        {
            if (entity?.Mentions is null) return;

            foreach (var mention in entity.Mentions)
            {
                if (mention.UserId == authorUserId) continue;

                _notifications.MentionReceived(
                    mention.UserId,
                    authorUserId,
                    $"{entity.Id}",
                    PlatformContexts.Comment);
            }
        }
    }
}
