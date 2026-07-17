using System.Linq;
using System.Threading.Tasks;
using Following.Application.Dtos;
using Following.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;

namespace Following.API.Features.Followings
{
    public record GetPendingRequestsQuery(FollowingContext _context, IIdentityService _auth) : Query<int, GetPendingRequestsDto>
    {
        public override async Task<GetPendingRequestsDto> Handle(int id)
        {
            var existing = await _context.FriendRequests.FirstOrDefaultAsync(e => e.User.Id == _auth.User.Id && e.Friend.Id == id);
            return new() { HasFriendRequestPending = existing is not null && !(existing.Approved ?? false) };
        }
    }
}
