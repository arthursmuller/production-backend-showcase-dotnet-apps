using Following.Domain.Repositories;
using Following.Application.Dtos;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using StarterEntities;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record AddFriendRequestCommand(
        IFollowingRepository Repository,
        IIdentityService Identity) : Command<RequestFriendshipDto, FriendRequest>
    {
        public override async Task<FriendRequest> Handle(
            RequestFriendshipDto dto,
            CancellationToken cancellationToken)
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
            var existing = await Repository.GetFriendRequest(user.Id, dto.UserId, cancellationToken);
            var areFriends = await Repository.IsFriend(user.Id, dto.UserId, cancellationToken);
            var request = FriendRequest.Create(
                user,
                new User(dto.UserId, string.Empty),
                existing,
                areFriends);

            var recipientAnalytics = await Repository.GetOrCreateAnalytics(
                dto.UserId, user.Id, cancellationToken);
            recipientAnalytics.IncrementFriendRequests();

            await Repository.Add(request, cancellationToken);
            await Repository.SaveChanges(cancellationToken);
            await pairMutation.CommitAsync(cancellationToken);
            return request;
        }
    }
}
