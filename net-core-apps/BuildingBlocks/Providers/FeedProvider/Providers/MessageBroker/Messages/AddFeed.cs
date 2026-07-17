using MessageBrokerCore.Messages;
using FeedProvider.Dtos;

namespace FeedProvider.Providers.MessageBroker.RabbitMq.Messages
{
    public class AddFeedMessage : IntegrationMessage
    {
        public UserConnectionsFeedDto Dto;

        public AddFeedMessage(UserConnectionsFeedDto dto)
        {
            Dto = dto;
        }
    }
}
