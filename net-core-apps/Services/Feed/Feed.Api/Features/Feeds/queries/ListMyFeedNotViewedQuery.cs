using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EfCoreFilters;
using Feed.Domain.AggregatesModel;
using Feed.Application.Dtos;
using Feed.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;


using Res = (System.Collections.Generic.IEnumerable<Feed.Domain.AggregatesModel.UserConnectionsFeed>, int count);

namespace Feed.API.Features.Feeds
{
    public record ListMyFeedNotViewedQuery(FeedContext _context, IIdentityService _auth, IServiceProvider _services) : Query<Res>
    {
        public override async Task<Res> Handle()
        {
            var (items, total) = await _context.UserConnectionsFeed.AsQueryable().AsNoTracking()
                .Where(e => e.UserId == _auth.User.Id && e.Post.IsPublicViewable && e.DateSeen == null)
                .Filter(new()
                {
                    SortBy = "date_desc",
                    PageNumber = 1,
                    PageSize = 7
                });

            await updateDateSeen(_services, items.Select(e => e.Id));
            return (items, total);
        }

        internal static async Task updateDateSeen(IServiceProvider services, IEnumerable<int> ids)
        {
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<FeedContext>();

            var itemsToUpdateLastSeen = ctx.UserConnectionsFeed?.Where(e => ids.Contains(e.Id));
            if (itemsToUpdateLastSeen is not null)
            {
                var date = DateTime.Now;
                foreach (var item in itemsToUpdateLastSeen)
                    item.DateSeen = date.AddSeconds(3);

                await ctx.SaveChangesAsync();
            }
        }
    }
}
