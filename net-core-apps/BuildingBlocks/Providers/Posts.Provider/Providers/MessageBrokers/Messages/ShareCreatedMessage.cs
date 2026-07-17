using MessageBrokerCore.Messages;
using StarterEntities;

namespace Posts.Provider
{
    public class ShareCreatedMessage : IntegrationMessage
    {
        public UpdateCommentCountDto Dto;

        public ShareCreatedMessage(UpdateCommentCountDto dto)
        {
            Dto = dto;
        }
    }
}
