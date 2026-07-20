using Following.Application.Providers.Time;
using Following.Application.Providers.Relationships;
using Following.Application.Dtos;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using MessageBrokerContracts.Following;
using StarterEntities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Subscriptions
{
    public record SetFriendsSubscriptionFeatureCommand(
        IFollowingRepository Repository,
        IClock Clock,
        IRelationshipEventWriter RelationshipEvents)
        : Command<SubscriptionFeatureUpdatedDto, FriendshipSubscriptionFeature?>
    {
        public override async Task<FriendshipSubscriptionFeature?> Handle(
            SubscriptionFeatureUpdatedDto dto,
            CancellationToken cancellationToken)
        {
            if (dto?.Owner?.Id is not > 0
                || dto.Requester?.Id is not > 0
                || dto.Owner.Id == dto.Requester.Id)
            {
                throw new FollowingException(
                    "Valid distinct subscription participants are required.");
            }

            await using var pairMutation = await Repository.AcquireRelationshipPairMutation(
                dto.Requester.Id,
                dto.Owner.Id,
                cancellationToken);
            await Repository.EnsureUsersNotErased(
                [dto.Requester.Id, dto.Owner.Id],
                cancellationToken);
            var existing = await Repository.GetSubscriptionFeature(
                dto.SubscriptionId,
                dto.Requester.Id,
                dto.Owner.Id,
                cancellationToken);

            var transition = FriendshipSubscriptionFeature.Apply(
                existing,
                dto.Owner.Id,
                dto.Requester.Id,
                dto.SubscriptionId,
                dto,
                Clock.UtcNow);

            if (transition.Kind == FriendshipSubscriptionFeature.TransitionKind.NoChange)
                return null;

            var entity = transition.Feature
                ?? throw new InvalidOperationException(
                    "A subscription mutation requires a feature entity.");

            if (transition.Kind == FriendshipSubscriptionFeature.TransitionKind.Remove)
            {
                Repository.Remove(entity);
            }
            else if (transition.Kind == FriendshipSubscriptionFeature.TransitionKind.Add)
            {
                await Repository.Add(entity, cancellationToken);

                var requester = new User(dto.Requester.Id, dto.Requester.Name);
                var owner = new User(dto.Owner.Id, dto.Owner.Name);
                var isBlocked = await Repository.IsBlockedEitherDirection(
                    requester.Id,
                    owner.Id,
                    cancellationToken);
                var friendships = isBlocked
                    ? System.Array.Empty<Friendship>()
                    : Friendship.CreatePairIfMissing(
                        requester,
                        owner,
                        await Repository.IsFriend(
                            requester.Id,
                            owner.Id,
                            cancellationToken));

                if (friendships.Count > 0)
                {
                    var requesterAnalytics = await Repository.GetOrCreateAnalytics(
                        requester.Id,
                        owner.Id,
                        cancellationToken);
                    var ownerAnalytics = await Repository.GetOrCreateAnalytics(
                        owner.Id,
                        requester.Id,
                        cancellationToken);
                    requesterAnalytics.IncrementFriends();
                    ownerAnalytics.IncrementFriends();
                    await Repository.AddRange(friendships, cancellationToken);
                    foreach (var friendship in friendships)
                    {
                        await RelationshipEvents.StageAsync(
                            new RelationshipTransition(
                                friendship.UserId,
                                friendship.FriendUserId,
                                RelationshipKinds.Friend,
                                RelationshipStates.Active,
                                "subscription.friendship.created"),
                            cancellationToken);
                    }
                }
            }

            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);
            return entity;
        }
    }
}
