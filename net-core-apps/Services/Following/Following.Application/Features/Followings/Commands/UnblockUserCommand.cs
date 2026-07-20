using Following.Application.Dtos;
using Following.Application.Providers.Relationships;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using MessageBrokerContracts.Following;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public sealed record UnblockUserCommand(
        IFollowingRepository Repository,
        IIdentityService Identity,
        IRelationshipEventWriter RelationshipEvents) : Command<int, bool>
    {
        public override async Task<bool> Handle(
            int blockedUserId,
            CancellationToken cancellationToken)
        {
            FollowingInputLimits.RequirePositiveId(blockedUserId, nameof(blockedUserId));
            var blockerUserId = Identity.User?.Id ?? 0;
            if (blockerUserId <= 0)
                throw new FollowingException("An authenticated user is required.", 403);
            if (blockerUserId == blockedUserId)
                throw new FollowingException("A user cannot unblock themselves.");

            await using var pairMutation = await Repository.AcquireRelationshipPairMutation(
                blockerUserId,
                blockedUserId,
                cancellationToken);
            await Repository.EnsureUsersNotErased(
                [blockerUserId, blockedUserId],
                cancellationToken);
            var existing = await Repository.GetBlock(
                blockerUserId,
                blockedUserId,
                cancellationToken);
            if (existing is null)
                return false;

            Repository.Remove(existing);
            await RelationshipEvents.StageAsync(
                new RelationshipTransition(
                    blockerUserId,
                    blockedUserId,
                    RelationshipKinds.Block,
                    RelationshipStates.Revoked,
                    "block.removed"),
                cancellationToken);
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);
            return true;
        }
    }
}
