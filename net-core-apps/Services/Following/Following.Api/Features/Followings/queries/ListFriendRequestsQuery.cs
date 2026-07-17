using EfCoreFilters;
using Following.Application.Dtos;
using Following.Infra.Persistence;
using IdentityProvider;
using System.Linq;
using System.Threading.Tasks;

using Res = (System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.FriendRequest>, int count);

namespace Following.API.Features.Followings
{
    public record ListFriendRequestsQuery(FollowingContext _context, IIdentityService _auth) : Query<FriendsRequestCriteria, Res>
    {
        public override async Task<Res> Handle(FriendsRequestCriteria criteria)
        {
            var query = _context.FriendRequests.AsQueryable();
            query = criteria.SentRequests ? query.Where(e => e.User.Id == _auth.User.Id) : query.Where(e => e.Friend.Id == _auth.User.Id);
            query = (criteria.Pending ?? false) ? query.Where(e => e.Approved == null) : query;
            return await query.Filter(criteria);
        }
    }
}
