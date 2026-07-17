using System;

namespace FeedProvider.Dtos
{
    public class RemoveUserConnectionsFeedDto
    {
        public string EntityId { get; set; }
        public string EntityType { get; set; }

        public RemoveUserConnectionsFeedDto(string entityId, string entityType)
        {
            EntityId = entityId;
            EntityType = entityType;
        }
    }
}
