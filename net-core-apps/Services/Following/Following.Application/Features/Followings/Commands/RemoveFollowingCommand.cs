using Following.Domain.Repositories;
using Following.Domain.Exceptions;
using Following.Application.Dtos;
using Following.Application.Providers.Relationships;
using MessageBrokerContracts.Following;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveFollowingCommand(
        IFollowingRepository Repository,
        IIdentityService Identity,
        IRelationshipEventWriter RelationshipEvents) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id, CancellationToken cancellationToken)
        {
            FollowingInputLimits.RequirePositiveId(id, nameof(id));
            var actor = Identity.User;
            if (actor?.Id is not > 0)
                throw new FollowingException("An authenticated user is required.", 403);
            var user = new StarterEntities.User(actor.Id, actor.Name);
            await using var pairMutation = await Repository.AcquireRelationshipPairMutation(
                user.Id,
                id,
                cancellationToken);
            await Repository.EnsureUsersNotErased([user.Id, id], cancellationToken);
            var existing = await Repository.GetFollow(user.Id, id, cancellationToken);
            if (existing is null)
                throw new FollowingException("The follow relationship was not found.", 404);

            var followBack = await Repository.GetFollow(
                existing.FollowedUserId,
                user.Id,
                cancellationToken);
            existing.UnlinkReciprocal(followBack);

            var userAnalytics = await Repository.GetOrCreateAnalytics(
                user.Id,
                existing.FollowedUserId,
                cancellationToken);
            var followedAnalytics = await Repository.GetOrCreateAnalytics(
                existing.FollowedUserId,
                user.Id,
                cancellationToken);
            userAnalytics.DecrementFollowing();
            followedAnalytics.DecrementFollowers();

            Repository.Remove(existing);
            await RelationshipEvents.StageAsync(
                new RelationshipTransition(
                    existing.FollowedUserId,
                    existing.FollowerUserId,
                    RelationshipKinds.Follow,
                    RelationshipStates.Revoked,
                    "follow.removed"),
                cancellationToken);
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);

            return true;
        }
    }
}
