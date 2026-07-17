using Following.Application.Abstractions;
using Following.Domain.AggregatesModel;
using Following.Infra.Helpers;
using Microsoft.EntityFrameworkCore;
using StarterEntities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Following.Infra.Persistence
{
    public sealed class FollowingRepository(FollowingContext context) : IFollowingRepository
    {
        public Task<CategoryFollow> GetCategoryFollow(
            int userId,
            int? categoryId,
            string categoryName)
        {
            var query = context.CategoryFollows.Where(item => item.User.Id == userId);
            return categoryId.HasValue
                ? query.FirstOrDefaultAsync(item => item.Followed.Id == categoryId.Value)
                : query.FirstOrDefaultAsync(item => item.Followed.Name == categoryName);
        }

        public Task<Follow> GetFollow(int userId, int followedId) =>
            context.Follows.FirstOrDefaultAsync(item =>
                item.User.Id == userId && item.Followed.Id == followedId);

        public Task<bool> IsFollowing(int userId, int followedId) =>
            FollowingHelpers.IsFollowing(context, userId, followedId);

        public Task<FriendRequest> GetFriendRequest(int userId, int friendId) =>
            context.FriendRequests.FirstOrDefaultAsync(item =>
                item.User.Id == userId && item.Friend.Id == friendId);

        public Task<FriendRequest> GetFriendRequestForApproval(int requestId, int friendId) =>
            context.FriendRequests.FirstOrDefaultAsync(item =>
                item.Id == requestId && item.Friend.Id == friendId);

        public Task<bool> IsFriend(int userId, int friendId) =>
            FollowingHelpers.IsFriend(context, userId, friendId);

        public async Task<IReadOnlyList<Friendship>> GetFriendships(int userId, int friendId) =>
            await context.Friendships
                .Where(item =>
                    item.Friend.Id == friendId && item.User.Id == userId ||
                    item.User.Id == friendId && item.Friend.Id == userId)
                .ToListAsync();

        public Task<FriendshipSubscriptionFeature> GetSubscriptionFeature(
            string subscriptionId,
            int requesterId,
            int ownerId) =>
            context.FriendsSubscriptionsFeature.FirstOrDefaultAsync(item =>
                item.SubscriptionId == subscriptionId &&
                item.RequesterId == requesterId &&
                item.OwnerId == ownerId);

        public async Task Add<T>(T entity) where T : class =>
            await context.Set<T>().AddAsync(entity);

        public void Remove<T>(T entity) where T : class => context.Remove(entity);

        public void RemoveRange<T>(IEnumerable<T> entities) where T : class =>
            context.RemoveRange(entities);

        public async Task SaveChanges() => await context.SaveChangesAsync();

        public Task UpdateAnalytics(
            int userId,
            FollowingAnalyticsField field,
            bool increment) =>
            FollowingHelpers.UpdateAnalytics(
                context,
                userId,
                field switch
                {
                    FollowingAnalyticsField.Friends => FollowingHelpers.AnalyticsField.Friends,
                    FollowingAnalyticsField.FriendRequests => FollowingHelpers.AnalyticsField.FriendRequests,
                    FollowingAnalyticsField.Followers => FollowingHelpers.AnalyticsField.Followers,
                    FollowingAnalyticsField.Followings => FollowingHelpers.AnalyticsField.Followings,
                    FollowingAnalyticsField.CategoryFollowings => FollowingHelpers.AnalyticsField.CategoryFollowings,
                    _ => throw new System.ArgumentOutOfRangeException(nameof(field), field, null)
                },
                increment);

        public Task<Friendship> AddFriendship(User user, User friend) =>
            FollowingHelpers.AddFriendship(context, user, friend);
    }
}
