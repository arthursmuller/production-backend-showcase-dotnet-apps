using CategoriesProvider.Abstractions;
using Comment.Application.Providers.Notifications;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Events;
using FeedProvider.Abstractions;
using IdentityProvider;
using Mediator;
using Posts.Provider.Abstractions;
using StarterEntities;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.EventHandlers.Comment
{
    public sealed class CommentAddedEventHandler(
        IFeedService _feed,
        IPostsService _posts,
        ICategoriesService _categories,
        IIdentityService _auth,
        INotifications _notifications) : INotificationHandler<CommentAddedEvent>
    {
        public ValueTask Handle(CommentAddedEvent notification, CancellationToken cancellationToken)
        {
            var entity = notification.Entity;
            var userId = _auth.User.Id;

            _posts.Broker.CommentCreated(
                new(entity.IdOfRelatedEntity, true, userId),
                entity.TypeOfRelatedEntity.ToLower());

            if (entity.VisibilityLevel.IsVisible())
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
