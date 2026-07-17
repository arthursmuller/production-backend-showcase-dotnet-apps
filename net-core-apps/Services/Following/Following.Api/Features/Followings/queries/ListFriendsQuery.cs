using System.Linq;
using System.Threading.Tasks;
using EfCoreFilters;
using Following.Application.Dtos;
using Following.Infra.Persistence;
using IdentityProvider;

using Res = (System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.Friendship>, int count);

namespace Following.API.Features.Followings
{
    public record ListFriendsQuery(FollowingContext _context, IIdentityService _auth) : Query<FriendsCriteria, Res>
    {
        public override async Task<Res> Handle(FriendsCriteria criteria)
        {
            var query = _context.Friendships.AsQueryable();
            query = query.Where(e => e.User.Id == (criteria.UserId ?? _auth.User.Id));
            query = criteria.Search is not null ? query.Where(e => e.Friend.FullName.Contains(criteria.Search)) : query;
            return await query.Filter(criteria);
        }
    }
}
