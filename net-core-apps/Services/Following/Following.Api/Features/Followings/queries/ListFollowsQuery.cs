using System.Linq;
using System.Threading.Tasks;
using EfCoreFilters;
using Following.Application.Dtos;
using Following.Infra.Persistence;

using Res = (System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.Follow>, int count);

namespace Following.API.Features.Followings
{
    public record ListFollowsQuery(FollowingContext _context) : Query<FollowsCriteria, Res>
    {
        public override async Task<Res> Handle(FollowsCriteria criteria)
        {
            var query = _context.Follows.AsQueryable();
            query = criteria.Following ? query.Where(e => e.User.Id == criteria.UserId) : query.Where(e => e.Followed.Id == criteria.UserId);
            return await query.Filter(criteria);
        }
    }
}
