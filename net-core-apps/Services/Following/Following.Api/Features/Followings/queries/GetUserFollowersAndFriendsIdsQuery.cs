using System.Threading.Tasks;
using Following.Application.Dtos;
using Following.Infra.Helpers;
using Following.Infra.Persistence;

namespace Following.API.Features.Followings
{
    public record GetUserFollowersAndFriendsIdsQuery(FollowingContext _context) : Query<int, GetUserFollowersAndFriendsIdDto>
    {
        public override async Task<GetUserFollowersAndFriendsIdDto> Handle(int userId) => new()
        {
            Friends = await FollowingHelpers.ListFriendsIds(_context, userId),
            Followers = await FollowingHelpers.ListFollowersId(_context, userId),
            Categories = await FollowingHelpers.ListCategoryIds(_context, userId),
        };
    }
}
