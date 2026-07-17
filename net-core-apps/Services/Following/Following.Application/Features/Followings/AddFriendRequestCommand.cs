using Following.Application.Abstractions;
using Following.Application.Dtos;
using Following.Domain.AggregatesModel;
using StarterEntities;
using System;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record AddFriendRequestCommand(
        IFollowingRepository Repository,
        IIdentity Identity) : Command<RequestFriendshipDto, FriendRequest>
    {
        public override async Task<FriendRequest> Handle(RequestFriendshipDto dto)
        {
            var user = Identity.User;
            var existing = await Repository.GetFriendRequest(user.Id, dto.UserId);
            var areFriends = await Repository.IsFriend(user.Id, dto.UserId);

            if (areFriends)
                throw new Exception("Already Accepted");
            if (existing is not null)
                throw new Exception("Request Already sent");

            var request = new FriendRequest
            {
                User = user,
                Friend = new User(dto.UserId, null)
            };

            await Repository.Add(request);
            await Repository.SaveChanges();
            return request;
        }
    }
}
