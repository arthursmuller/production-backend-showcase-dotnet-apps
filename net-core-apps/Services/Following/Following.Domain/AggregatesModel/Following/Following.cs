using StarterEntities;
using System;

namespace Following.Domain.AggregatesModel
{
    public class FriendRequest : BaseEntity
    {
        public User User { get; set; }
        public User Friend { get; set; }
        public bool? Approved { get; set; }
    }

    public class Friendship : BaseEntity
    {
        public User User { get; set; }
        public User Friend { get; set; }
        public Friendship(User user, User friend)
        {
            User = user;
            Friend = friend;
        }
        private Friendship() { }
    }

    public class Follow : BaseEntity
    {
        public User User { get; set; }
        public User Followed { get; set; }
        public bool IsFollowedBack { get; set; }
        public Follow(User user, User followed)
        {
            User = user;
            Followed = followed;
            IsFollowedBack = false;
        }
        private Follow() { }
    }

    public class CategoryFollow : BaseEntity
    {
        public User User { get; set; }
        public CategoryRecord Followed { get; set; }

        public CategoryFollow(User user, CategoryRecord followed)
        {
            User = user;
            Followed = followed;
        }
        private CategoryFollow() { }
    }

    public class UserRelationAnalytics : BaseEntity
    {
        public int TotalFriends { get; set; }
        public int TotalFollowers { get; set; }
        public int TotalFollowing { get; set; }
        public int TotalCategoriesFollowed { get; set; }
        public int TotalFriendsRequests { get; set; }

        public UserRelationAnalytics(int id) : base (id) { }
    }

    public class FriendshipSubscriptionFeature : CommomSubscriptionFeature
    {
        public FriendshipSubscriptionFeature(int ownerId, int requesterId, string subscriptionId, DateTime endDate)
            : base(ownerId, requesterId, subscriptionId, endDate) { }
    }
}
