using Following.Domain.AggregatesModel;
using StarterEntities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Following.Application.Abstractions
{
    public enum FollowingAnalyticsField
    {
        Friends,
        FriendRequests,
        Followers,
        Followings,
        CategoryFollowings
    }

    public interface IFollowingRepository
    {
        Task<CategoryFollow> GetCategoryFollow(int userId, int? categoryId, string categoryName);
        Task<Follow> GetFollow(int userId, int followedId);
        Task<bool> IsFollowing(int userId, int followedId);
        Task<FriendRequest> GetFriendRequest(int userId, int friendId);
        Task<FriendRequest> GetFriendRequestForApproval(int requestId, int friendId);
        Task<bool> IsFriend(int userId, int friendId);
        Task<IReadOnlyList<Friendship>> GetFriendships(int userId, int friendId);
        Task<FriendshipSubscriptionFeature> GetSubscriptionFeature(
            string subscriptionId,
            int requesterId,
            int ownerId);

        Task Add<T>(T entity) where T : class;
        void Remove<T>(T entity) where T : class;
        void RemoveRange<T>(IEnumerable<T> entities) where T : class;
        Task SaveChanges();
        Task UpdateAnalytics(int userId, FollowingAnalyticsField field, bool increment);
        Task<Friendship> AddFriendship(User user, User friend);
    }
}
