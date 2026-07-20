using Comment.Application.Features.Interactions;
using Comment.Application.Providers.Interactions;
using Comment.Application.Dtos.Interactions;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Events;
using Interaction.Contracts;
using Mediator;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.EventHandlers.Comments
{
    public sealed class CommentInteractionStateChangedEventHandler(
        ICommentInteractionEventPort events)
        : INotificationHandler<CommentInteractionStateChangedEvent>
    {
        public async ValueTask Handle(
            CommentInteractionStateChangedEvent notification,
            CancellationToken cancellationToken)
        {
            var comment = notification.Comment;
            var messageId = CommentInteractionMessageIdentity.Create(
                "comment-resource-state",
                comment.Id,
                notification.SourceVersion,
                notification.Deleted);
            await events.StageResourceStateAsync(
                new CommentResourceStateChanged(
                    comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    notification.Deleted
                        ? ResourceLifecycle.Deleted
                        : ResourceLifecycle.Active,
                    MapVisibility(comment.VisibilityLevelId),
                    notification.SourceVersion,
                    notification.OccurredAtUtc),
                messageId,
                cancellationToken);
            if (notification.IsNew)
            {
                var contributionMessageId = CommentInteractionMessageIdentity.Create(
                    "comment-contribution", comment.Id, 1);
                await events.StageContributionAsync(
                    new ResourceContributionChanged(
                        "comment",
                        comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        0,
                        0,
                        1,
                        notification.OccurredAtUtc),
                    contributionMessageId,
                    cancellationToken);
            }
        }

        private static ResourceVisibility MapVisibility(int visibilityLevelId) =>
            visibilityLevelId == VisibilityLevel.Global.Id
                ? ResourceVisibility.Public
                : visibilityLevelId == VisibilityLevel.Private.Id
                    ? ResourceVisibility.Private
                    : ResourceVisibility.Restricted;
    }
}
