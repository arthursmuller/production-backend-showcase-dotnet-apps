using MessageBrokerCore.Messages;
using FeedProvider.Dtos;

namespace FeedProvider.Providers.MessageBroker.RabbitMq.Messages
{
    public class RemoveFeedMessage : IntegrationMessage
    {
        public RemoveUserConnectionsFeedDto Dto;

        public RemoveFeedMessage(RemoveUserConnectionsFeedDto dto)
        {
            Dto = dto;
        }
    }
}
