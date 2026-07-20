using Following.Application.Dtos;
using Following.Application.Providers.Relationships;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using MessageBrokerContracts.Following;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    /// <summary>
    /// Activates a directional block and atomically removes every relationship that could grant
    /// either participant access. All downstream revocations are staged in the same EF bus outbox
    /// transaction as the authority rows.
    /// </summary>
    public sealed record BlockUserCommand(
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
                throw new FollowingException("A user cannot block themselves.");

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
            if (existing is not null)
                return false;

            var outgoingFollow = await Repository.GetFollow(
                blockerUserId,
                blockedUserId,
                cancellationToken);
            var incomingFollow = await Repository.GetFollow(
                blockedUserId,
                blockerUserId,
                cancellationToken);
            var friendships = await Repository.GetFriendships(
                blockerUserId,
                blockedUserId,
                cancellationToken);
            var pendingRequest = await Repository.GetFriendRequest(
                blockerUserId,
                blockedUserId,
                cancellationToken);

            await RemoveFollowsAsync(
                outgoingFollow,
                incomingFollow,
                blockerUserId,
                blockedUserId,
                cancellationToken);
            await RemoveFriendshipAsync(
                friendships,
                blockerUserId,
                blockedUserId,
                cancellationToken);
            await RemovePendingRequestAsync(pendingRequest, cancellationToken);

            var block = UserBlock.Create(blockerUserId, blockedUserId, existing: null);
            await Repository.Add(block, cancellationToken);
            await RelationshipEvents.StageAsync(
                new RelationshipTransition(
                    blockerUserId,
                    blockedUserId,
                    RelationshipKinds.Block,
                    RelationshipStates.Blocked,
                    "block.created"),
                cancellationToken);
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);
            return true;
        }

        private async Task RemoveFollowsAsync(
            Follow? outgoing,
            Follow? incoming,
            int blockerUserId,
            int blockedUserId,
            CancellationToken cancellationToken)
        {
            if (outgoing is not null)
            {
                Repository.Remove(outgoing);
                var blockerAnalytics = await Repository.GetOrCreateAnalytics(
                    blockerUserId,
                    blockedUserId,
                    cancellationToken);
                var blockedAnalytics = await Repository.GetOrCreateAnalytics(
                    blockedUserId,
                    blockerUserId,
                    cancellationToken);
                blockerAnalytics.DecrementFollowing();
                blockedAnalytics.DecrementFollowers();
            }

            if (incoming is not null)
            {
                Repository.Remove(incoming);
                var blockedAnalytics = await Repository.GetOrCreateAnalytics(
                    blockedUserId,
                    blockerUserId,
                    cancellationToken);
                var blockerAnalytics = await Repository.GetOrCreateAnalytics(
                    blockerUserId,
                    blockedUserId,
                    cancellationToken);
                blockedAnalytics.DecrementFollowing();
                blockerAnalytics.DecrementFollowers();
            }

            // Revoke both durable streams even when an authority row is absent. This closes a
            // stale downstream projection and prevents a later unblock from appearing to restore
            // a relationship which the block was required to destroy.
            await StageFollowRevocationAsync(
                blockerUserId,
                blockedUserId,
                cancellationToken);
            await StageFollowRevocationAsync(
                blockedUserId,
                blockerUserId,
                cancellationToken);
        }

        private Task StageFollowRevocationAsync(
            int followerUserId,
            int followedUserId,
            CancellationToken cancellationToken) =>
            RelationshipEvents.StageAsync(
                new RelationshipTransition(
                    followedUserId,
                    followerUserId,
                    RelationshipKinds.Follow,
                    RelationshipStates.Revoked,
                    "follow.blocked"),
                cancellationToken);

        private async Task RemoveFriendshipAsync(
            IReadOnlyList<Friendship> friendships,
            int blockerUserId,
            int blockedUserId,
            CancellationToken cancellationToken)
        {
            if (friendships.Count > 0)
            {
                Repository.RemoveRange(friendships);
                var blockerAnalytics = await Repository.GetOrCreateAnalytics(
                    blockerUserId,
                    blockedUserId,
                    cancellationToken);
                var blockedAnalytics = await Repository.GetOrCreateAnalytics(
                    blockedUserId,
                    blockerUserId,
                    cancellationToken);
                blockerAnalytics.DecrementFriends();
                blockedAnalytics.DecrementFriends();
            }

            await StageFriendRevocationAsync(blockerUserId, blockedUserId, cancellationToken);
            await StageFriendRevocationAsync(blockedUserId, blockerUserId, cancellationToken);
        }

        private Task StageFriendRevocationAsync(
            int ownerUserId,
            int viewerUserId,
            CancellationToken cancellationToken) =>
            RelationshipEvents.StageAsync(
                new RelationshipTransition(
                    ownerUserId,
                    viewerUserId,
                    RelationshipKinds.Friend,
                    RelationshipStates.Revoked,
                    "friendship.blocked"),
                cancellationToken);

        private async Task RemovePendingRequestAsync(
            FriendRequest? pendingRequest,
            CancellationToken cancellationToken)
        {
            if (pendingRequest is null)
                return;

            Repository.Remove(pendingRequest);
            var recipientAnalytics = await Repository.GetOrCreateAnalytics(
                pendingRequest.RecipientUserId,
                pendingRequest.SenderUserId,
                cancellationToken);
            recipientAnalytics.DecrementFriendRequests();
        }
    }
}
