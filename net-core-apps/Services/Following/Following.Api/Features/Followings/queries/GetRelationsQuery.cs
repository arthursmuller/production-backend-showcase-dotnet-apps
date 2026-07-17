using System.Threading.Tasks;
using Following.Application.Dtos;
using Following.Infra.Helpers;
using Following.Infra.Persistence;
using IdentityProvider;

namespace Following.API.Features.Followings
{
    public record GetRelationsQuery(FollowingContext _context, IIdentityService _auth) : Query<int, GetRelationsDto>
    {
        public override async Task<GetRelationsDto> Handle(int id)
            => new()
            {
                IsFriend = await FollowingHelpers.IsFriend(_context, _auth.User.Id, id),
                IsFriendRequestSent = await FollowingHelpers.IsFriendRequestSent(_context, _auth.User.Id, id),
                IsFollowing = await FollowingHelpers.IsFollowing(_context, _auth.User.Id, id),
            };
    }
}
