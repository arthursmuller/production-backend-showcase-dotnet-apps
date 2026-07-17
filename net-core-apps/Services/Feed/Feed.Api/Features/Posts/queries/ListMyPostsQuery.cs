using System.Linq;
using System.Threading.Tasks;
using EfCoreFilters;
using Feed.Domain.AggregatesModel;
using Feed.Application.Dtos;
using Feed.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;

using Res = (System.Collections.Generic.IEnumerable<Feed.Domain.AggregatesModel.Post>, int count);

namespace Feed.API.Features.Posts
{
    public record ListMyPostsQuery(FeedContext _context, IIdentityService _auth) : Query<PostsCriteria, Res>
    {
        public override async Task<Res> Handle(PostsCriteria req)
        {
            var (items, total) = await new DbFilters<Post>(
                    _context.Set<Post>().AsQueryable().AsNoTracking()
                        .Where(e => e.UserId == _auth.User.Id))
                .New(req.Types, (e, val) => val.Any(type => type.ToLower() == e.Type.ToLower()))
                .Build()
                .Filter(req);

            return (items, total);
        }
    }
}
