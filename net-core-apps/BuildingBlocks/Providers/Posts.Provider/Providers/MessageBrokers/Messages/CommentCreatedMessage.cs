using MessageBrokerCore.Messages;
using StarterEntities;

namespace Posts.Provider
{
    public class CommentCreatedMessage : IntegrationMessage
    {
        public UpdateCommentCountDto Dto;

        public CommentCreatedMessage(UpdateCommentCountDto dto)
        {
            Dto = dto;
        }
    }
}
