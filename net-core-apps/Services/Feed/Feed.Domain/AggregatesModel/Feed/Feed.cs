using RichEnumeration;
using StarterEntities;
using System;
using System.Collections.Generic;

namespace Feed.Domain.AggregatesModel
{
    public class Post : BaseEntity 
    {
        public string EntityId { get; set; }
        public int? UserId { get; set; }
        public string Type { get; set; }
        public bool IsPublicViewable { get; set; }
        
        public Post(DateTime createdDate) : base(createdDate) { }
        public Post() { }
    }

    public class UserConnectionsFeed : BaseEntity
    {
        public int? UserId { get; set; }
        public string Description { get; set; }
        public string EventTrigger { get; set; }
        public DateTime? DateSeen { get; set; }
        public Post Post { get; set; }

        public UserConnectionsFeed() { }
    }

    public class FeedVisibilityLevel : Enumeration
    {
        public string Description { get; set; }

        public static FeedVisibilityLevel Global = new FeedVisibilityLevel(1, nameof(Global).ToLowerInvariant(), "Todos");
        public static FeedVisibilityLevel Friends = new FeedVisibilityLevel(2, nameof(Friends).ToLowerInvariant(), "Amigos");
        public static FeedVisibilityLevel FriendsOfFriends = new FeedVisibilityLevel(3, nameof(FriendsOfFriends).ToLowerInvariant(), "Amigos de amigos");
        public static FeedVisibilityLevel Private = new FeedVisibilityLevel(4, nameof(Private).ToLowerInvariant(), "Eu");

        public FeedVisibilityLevel() { }

        public FeedVisibilityLevel(int id, string name, string description) : base(id, name)
        {
            Description = description;
        }

        public static IEnumerable<FeedVisibilityLevel> List() => new[] { Global, Friends, FriendsOfFriends, Private };
    }
}
