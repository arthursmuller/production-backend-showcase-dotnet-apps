using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CategoriesProvider.Abstractions;
using EfCoreFilters;
using Following.Domain.AggregatesModel;
using Following.Application.Dtos;
using Following.Infra.Helpers;
using Following.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;

using StarterEntities;
using PagedFriendRequestResponse = PagedResponse<System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.FriendRequest>>;
using PagedFriendshipResponse = PagedResponse<System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.Friendship>>;
using PagedFollowResponse = PagedResponse<System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.Follow>>;
using PagedCategoryFollowResponse = PagedResponse<System.Collections.Generic.IEnumerable<Following.Domain.AggregatesModel.CategoryFollow>>;
namespace Following.API.Features.Followings
{
    public record GetUserAnalyticsQuery(FollowingContext _context) : Query<int, UserRelationAnalytics>
    {
        public override Task<UserRelationAnalytics> Handle(int id) => FollowingHelpers.GetOrAddAnalytics(_context, id);
    }
}
