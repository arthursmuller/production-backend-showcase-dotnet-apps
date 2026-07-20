using Following.Domain.Repositories;
using Following.Domain.Exceptions;
using Following.Application.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveFriendRequestCommand(
        IFollowingRepository Repository,
        IIdentityService Identity) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id, CancellationToken cancellationToken)
        {
            FollowingInputLimits.RequirePositiveId(id, nameof(id));
            var identity = Identity.User;
            if (identity?.Id is not > 0)
                throw new FollowingException("An authenticated user is required.", 403);
            var actor = new StarterEntities.User(identity.Id, identity.Name);
            await using var pairMutation = await Repository.AcquireRelationshipPairMutation(
                actor.Id,
                id,
                cancellationToken);
            await Repository.EnsureUsersNotErased([actor.Id, id], cancellationToken);
            var existing = await Repository.GetFriendRequest(actor.Id, id, cancellationToken);
            if (existing is null)
                throw new FollowingException("The friend request was not found.", 404);

            existing.Cancel(actor);
            var recipientAnalytics = await Repository.GetOrCreateAnalytics(
                existing.RecipientUserId,
                existing.SenderUserId,
                cancellationToken);
            recipientAnalytics.DecrementFriendRequests();
            Repository.Remove(existing);
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);
            return true;
        }
    }
}
