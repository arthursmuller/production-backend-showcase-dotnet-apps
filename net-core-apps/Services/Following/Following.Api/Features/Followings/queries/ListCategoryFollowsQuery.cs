using EfCoreFilters;
using Following.Application.Dtos;
using Following.Infra.Persistence;
using System.Linq;
using System.Threading.Tasks;

using Res = (System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.CategoryFollow>, int count);

namespace Following.API.Features.Followings
{
    public record ListCategoryFollowsQuery(FollowingContext _context) : Query<CategoryFollowsCriteria, Res>
    {
        public override async Task<Res> Handle(CategoryFollowsCriteria criteria)
        {
            var query = _context.CategoryFollows.AsQueryable();
            query = query.Where(e => e.User.Id == criteria.UserId);
            return await query.Filter(criteria);
        }
    }
}
