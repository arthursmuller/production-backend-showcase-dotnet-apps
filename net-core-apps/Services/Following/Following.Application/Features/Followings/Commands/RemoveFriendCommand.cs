using Following.Domain.Repositories;
using Following.Domain.AggregatesModel;
using Following.Application.Dtos;
using Following.Application.Providers.Relationships;
using MessageBrokerContracts.Following;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveFriendCommand(
        IFollowingRepository Repository,
        IIdentityService Identity,
        IRelationshipEventWriter RelationshipEvents) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id, CancellationToken cancellationToken)
        {
            FollowingInputLimits.RequirePositiveId(id, nameof(id));
            var userId = Identity.User?.Id ?? 0;
            if (userId <= 0)
                throw new Following.Domain.Exceptions.FollowingException(
                    "An authenticated user is required.",
                    403);
            await using var pairMutation = await Repository.AcquireRelationshipPairMutation(
                userId,
                id,
                cancellationToken);
            await Repository.EnsureUsersNotErased([userId, id], cancellationToken);
            var friendships = Friendship.ValidatePairForRemoval(
                await Repository.GetFriendships(userId, id, cancellationToken),
                userId,
                id);

            var userAnalytics = await Repository.GetOrCreateAnalytics(
                userId, id, cancellationToken);
            var friendAnalytics = await Repository.GetOrCreateAnalytics(
                id, userId, cancellationToken);
            userAnalytics.DecrementFriends();
            friendAnalytics.DecrementFriends();

            Repository.RemoveRange(friendships);
            foreach (var friendship in friendships)
            {
                await RelationshipEvents.StageAsync(
                    new RelationshipTransition(
                        friendship.UserId,
                        friendship.FriendUserId,
                        RelationshipKinds.Friend,
                        RelationshipStates.Revoked,
                        "friendship.removed"),
                    cancellationToken);
            }
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);

            return true;
        }
    }
}
