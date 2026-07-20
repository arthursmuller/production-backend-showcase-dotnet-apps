using Following.Domain.Repositories;
using Following.Application.Dtos;
using Following.Application.Providers.Notifications;
using Following.Application.Providers.Relationships;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using MessageBrokerContracts.Following;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record ApproveFriendRequestCommand(
        IFollowingRepository Repository,
        IIdentityService Identity,
        NotificationProvider Notifications,
        IRelationshipEventWriter RelationshipEvents) : Command<ApproveFriendRequestInput, bool>
    {
        public override async Task<bool> Handle(
            ApproveFriendRequestInput input,
            CancellationToken cancellationToken)
        {
            FollowingInputLimits.RequirePositiveId(input.RequestId, nameof(input.RequestId));
            var actor = Identity.User;
            if (actor?.Id is not > 0)
                throw new FollowingException("An authenticated user is required.", 403);
            var user = new StarterEntities.User(actor.Id, actor.Name);
            var existing = await Repository.GetFriendRequestForApproval(
                input.RequestId,
                user.Id,
                cancellationToken);
            if (existing is null)
                throw new FollowingException("The friend request was not found.", 404);

            await using var pairMutation = await Repository.AcquireRelationshipPairMutation(
                existing.SenderUserId,
                existing.RecipientUserId,
                cancellationToken);
            await Repository.EnsureUsersNotErased(
                [existing.SenderUserId, existing.RecipientUserId],
                cancellationToken);
            existing = await Repository.GetFriendRequestForApproval(
                input.RequestId,
                user.Id,
                cancellationToken);
            if (existing is null)
                throw new FollowingException("The friend request was not found.", 404);

            if (await Repository.IsBlockedEitherDirection(
                existing.SenderUserId,
                existing.RecipientUserId,
                cancellationToken))
            {
                throw new FollowingException(
                    "A blocked user relationship cannot be approved.",
                    409);
            }

            var approved = existing.Decide(user, input.Decision);
            var recipientAnalytics = await Repository.GetOrCreateAnalytics(
                existing.RecipientUserId,
                existing.SenderUserId,
                cancellationToken);
            recipientAnalytics.DecrementFriendRequests();

            if (!approved)
            {
                Repository.Remove(existing);
                await Repository.SaveChanges(cancellationToken);
                await pairMutation.CommitAsync(cancellationToken);
                return false;
            }

            var alreadyFriends = await Repository.IsFriend(
                existing.SenderUserId,
                user.Id,
                cancellationToken);
            var friendships = Friendship.CreatePair(
                new StarterEntities.User(
                    existing.SenderUserId,
                    existing.SenderDisplayName ?? string.Empty),
                user,
                alreadyFriends);
            var requesterAnalytics = await Repository.GetOrCreateAnalytics(
                existing.SenderUserId,
                existing.RecipientUserId,
                cancellationToken);
            requesterAnalytics.IncrementFriends();
            recipientAnalytics.IncrementFriends();

            await Repository.AddRange(friendships, cancellationToken);
            Repository.Remove(existing);
            await Notifications.FriendRequestAcceptedAsync(
                existing.SenderUserId,
                existing.RecipientUserId,
                cancellationToken);
            foreach (var friendship in friendships)
            {
                await RelationshipEvents.StageAsync(
                    new RelationshipTransition(
                        friendship.UserId,
                        friendship.FriendUserId,
                        RelationshipKinds.Friend,
                        RelationshipStates.Active,
                        "friendship.created"),
                    cancellationToken);
            }
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);
            return true;
        }
    }
}
