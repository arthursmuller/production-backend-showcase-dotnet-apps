using System;

namespace FeedProvider.Dtos
{
    public class ChangeVisibilityDto
    {
        public string EntityId { get; set; }
        public string EntityType { get; set; }
        public bool IsPublic { get; set; }

        public ChangeVisibilityDto(string entityId, string entityType, bool isPublic)
        {
            EntityId = entityId;
            EntityType = entityType;
            IsPublic = isPublic;
        }
    }
}
