using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Following.Domain.AggregatesModel;
using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using StarterEntities;

namespace Following.Infra.Helpers
{
    public static class FollowingHelpers
    {
        public static async Task<bool> IsFriend(FollowingContext context, int userId, int friendId)
            => await context.Friendships.AsNoTracking().AnyAsync(e =>
                (e.User.Id == userId && e.Friend.Id == friendId) ||
                (e.User.Id == friendId && e.User.Id == userId));

        public static async Task<bool> IsFollowing(FollowingContext context, int userId, int followedId)
            => await context.Follows.AsNoTracking().AnyAsync(e => e.User.Id == userId && e.Followed.Id == followedId);

        public static async Task<bool> IsFriendRequestSent(FollowingContext context, int userId, int friendId)
            => await context.FriendRequests.AsNoTracking().AnyAsync(e =>
                (e.User.Id == userId && e.Friend.Id == friendId) || (e.User.Id == friendId && e.User.Id == userId));

        public static async Task<int[]> ListFriendsIds(FollowingContext context, int userId)
            => await context.Friendships.AsNoTracking().Where(e => e.User.Id == userId).Select(e => e.Friend.Id).ToArrayAsync();

        public static async Task<int[]> ListFollowersId(FollowingContext context, int userId)
            => await context.Follows.AsNoTracking().Where(e => e.Followed.Id == userId).Select(e => e.User.Id).ToArrayAsync();

        public static async Task<int[]> ListCategoryIds(FollowingContext context, int userId)
            => await context.CategoryFollows.AsNoTracking().Where(e => e.User.Id == userId).Select(e => e.Followed.Id).ToArrayAsync();

        public static async Task<UserRelationAnalytics> GetOrAddAnalytics(FollowingContext context, int id)
        {
            var rating = await context.UserRelationsAnalytics.FirstOrDefaultAsync(e => e.Id == id);
            if (rating is null)
            {
                var added = context.UserRelationsAnalytics.Add(new UserRelationAnalytics(id));
                await context.SaveChangesAsync();
                rating = added?.Entity;
            }
            return rating;
        }

        public enum AnalyticsField { Friends, FriendRequests, Followers, Followings, CategoryFollowings }

        public static async Task UpdateAnalytics(FollowingContext context, int id, AnalyticsField field, bool increment)
        {
            var existing = await GetOrAddAnalytics(context, id);
            switch (field)
            {
                case AnalyticsField.Friends:
                    if (increment) existing.TotalFriends++; else existing.TotalFriends--;
                    break;
                case AnalyticsField.FriendRequests:
                    if (increment) existing.TotalFriendsRequests++; else existing.TotalFriendsRequests--;
                    break;
                case AnalyticsField.Followers:
                    if (increment) existing.TotalFollowers++; else existing.TotalFollowers--;
                    break;
                case AnalyticsField.Followings:
                    if (increment) existing.TotalFollowing++; else existing.TotalFollowing--;
                    break;
                case AnalyticsField.CategoryFollowings:
                    if (increment) existing.TotalCategoriesFollowed++; else existing.TotalCategoriesFollowed--;
                    break;
            }
            await context.SaveChangesAsync();
        }

        public static async Task<Friendship> AddFriendship(FollowingContext context, User user, User friend)
        {
            if (!await IsFriend(context, user.Id, friend.Id))
            {
                var friends = new[] { new Friendship(user, friend), new Friendship(friend, user) };
                await context.AddRangeAsync(friends);
                await context.SaveChangesAsync();

                await UpdateAnalytics(context, friends[0].User.Id, AnalyticsField.Friends, true);
                await UpdateAnalytics(context, friends[1].User.Id, AnalyticsField.Friends, true);

                return friends[0];
            }
            return default;
        }
    }
}
