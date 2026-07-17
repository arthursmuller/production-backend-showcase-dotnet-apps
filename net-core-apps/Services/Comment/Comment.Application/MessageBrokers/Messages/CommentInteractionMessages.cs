using MessageBrokerCore.Messages;
using StarterEntities;
using System;

namespace Comment.Application.MessageBrokers.Messages
{
    public class CommentCreatedMessage : IntegrationMessage
    {
        public UpdateCommentCountDto Dto { get; set; }
    }

    public class CommentSharedMessage : IntegrationMessage
    {
        public UpdateCommentCountDto Dto { get; set; }
    }

    public class BookmarkChangedMessage : IntegrationMessage
    {
        public BookmarkChangedDto Dto { get; set; }
    }

    public class BookmarkChangedDto
    {
        public string IdOfRelatedEntity { get; set; } = string.Empty;
        public string TypeOfRelatedEntity { get; set; } = string.Empty;
        public int OwnerUserId { get; set; }
        public long GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public bool Created { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
