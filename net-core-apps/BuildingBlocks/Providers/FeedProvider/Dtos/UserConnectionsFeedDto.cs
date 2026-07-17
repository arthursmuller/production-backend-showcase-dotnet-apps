using System;

namespace FeedProvider.Dtos
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

        public UserConnectionsFeedDto(int userId, string description, string eventTrigger, string entityId, int? entityOwnerUserId, string entityType, DateTime createdDate)
        {
            UserId = userId;
            Description = description;
            EventTrigger = eventTrigger;
            EntityId = entityId;
            EntityOwnerUserId = entityOwnerUserId;
            EntityType = entityType;
            CreatedDate = createdDate;
        }
    }
}
