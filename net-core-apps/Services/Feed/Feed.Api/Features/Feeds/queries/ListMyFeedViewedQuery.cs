using System;
using System.Linq;
using System.Threading.Tasks;
using EfCoreFilters;
using Feed.Domain.AggregatesModel;
using Feed.Application.Dtos;
using Feed.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;

using Res = (System.Collections.Generic.IEnumerable<Feed.Domain.AggregatesModel.UserConnectionsFeed>, int count);

namespace Feed.API.Features.Feeds
{
    public record ListMyFeedViewedQuery(FeedContext _context, IIdentityService _auth) : Query<FeedViewedCriteria, Res>
    {
        public override async Task<Res> Handle(FeedViewedCriteria req)
        {
            req.SortBy = "date_desc";
            var (items, total) = await new DbFilters<UserConnectionsFeed>(
                    _context.UserConnectionsFeed.AsQueryable().AsNoTracking()
                        .Where(e => e.UserId == _auth.User.Id && e.Post.IsPublicViewable && e.DateSeen != null))
                .New(req.IdsToExclude, (e, val) => !val.Contains(e.Id))
                .New(req.Types, (e, val) => val.Any(type => type.ToLower() == e.Post.Type.ToLower()))
                .Build()
                .Filter(req);

            return (items, total);
        }
    }
}
