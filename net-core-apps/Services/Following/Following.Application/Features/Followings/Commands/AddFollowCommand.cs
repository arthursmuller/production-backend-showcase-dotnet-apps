using Following.Domain.Repositories;
using Following.Application.Dtos;
using Following.Application.Providers.Notifications;
using Following.Application.Providers.Relationships;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using MessageBrokerContracts.Following;
using StarterEntities;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record AddFollowCommand(
        IFollowingRepository Repository,
        IIdentityService Identity,
        NotificationProvider Notifications,
        IRelationshipEventWriter RelationshipEvents) : Command<FollowDto, Follow>
    {
        public override async Task<Follow> Handle(FollowDto dto, CancellationToken cancellationToken)
        {
            FollowingInputLimits.RequirePositiveId(dto.UserId, nameof(dto.UserId));
            var actor = Identity.User;
            if (actor?.Id is not > 0)
                throw new FollowingException("An authenticated user is required.", 403);
            var user = new StarterEntities.User(actor.Id, actor.Name);
            await using var pairMutation = await Repository.AcquireRelationshipPairMutation(
                user.Id,
                dto.UserId,
                cancellationToken);
            await Repository.EnsureUsersNotErased([user.Id, dto.UserId], cancellationToken);
            if (await Repository.IsBlockedEitherDirection(user.Id, dto.UserId, cancellationToken))
                throw new FollowingException("A blocked user relationship cannot be created.", 409);
            var existing = await Repository.GetFollow(user.Id, dto.UserId, cancellationToken);
            var reciprocal = await Repository.GetFollow(dto.UserId, user.Id, cancellationToken);
            var follow = Follow.Create(user, new User(dto.UserId, string.Empty), existing);
            follow.LinkReciprocal(reciprocal);

            var userAnalytics = await Repository.GetOrCreateAnalytics(
                user.Id, dto.UserId, cancellationToken);
            var followedAnalytics = await Repository.GetOrCreateAnalytics(
                dto.UserId, user.Id, cancellationToken);
            userAnalytics.IncrementFollowing();
            followedAnalytics.IncrementFollowers();

            await Repository.Add(follow, cancellationToken);
            await Notifications.NewFollowerAsync(
                follow.FollowedUserId,
                follow.FollowerUserId,
                cancellationToken);
            await RelationshipEvents.StageAsync(
                new RelationshipTransition(
                    follow.FollowedUserId,
                    follow.FollowerUserId,
                    RelationshipKinds.Follow,
                    RelationshipStates.Active,
                    "follow.created"),
                cancellationToken);
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);
            return follow;
        }
    }
}
