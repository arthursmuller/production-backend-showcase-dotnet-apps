using MessageBrokerCore.Messages;
using FeedProvider.Dtos;

namespace FeedProvider.Providers.MessageBroker.RabbitMq.Messages
{
    public class ChangeVisibilityMessage : IntegrationMessage
    {
        public ChangeVisibilityDto Dto;

        public ChangeVisibilityMessage(ChangeVisibilityDto dto)
        {
            Dto = dto;
        }
    }
}
