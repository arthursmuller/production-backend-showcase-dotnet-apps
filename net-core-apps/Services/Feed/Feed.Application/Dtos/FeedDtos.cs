using System;

namespace Feed.Application.Dtos
{
    public class UserConnectionsFeedDto
    {
        public int UserId { get; set; }
        public string Description { get; set; }
        public string EventTrigger { get; set; }
        public string EntityId { get; set; }
        public int? EntityOwnerUserId { get; set; }
        public string EntityType { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class AddFeedItemsDto
    {
        public int UserId { get; set; }
        public int PostId { get; set; }
        public string Description { get; set; }
        public string EventTrigger { get; set; }

        public AddFeedItemsDto(int userId, int postId, string description, string eventTrigger)
        {
            UserId = userId;
            PostId = postId;
            Description = description;
            EventTrigger = eventTrigger;
        }

        public AddFeedItemsDto() { }
    }

    public class FeedViewedCriteria : CommomFilter
    {
        public string[] Types { get; set; }
        public int[] IdsToExclude { get; set; }
    }

    public class PostsCriteria : CommomFilter
    {
        public string[] Types { get; set; }
        public int UserId { get; set; }
    }

    public record RemovePostDto(string EntityType, string EntityId);

    public record ChangePostVisibilityDto(string EntityType, string EntityId, bool IsPublic);

}
