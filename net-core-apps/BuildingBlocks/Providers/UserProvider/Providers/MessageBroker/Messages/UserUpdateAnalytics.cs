using MessageBrokerCore.Messages;
using UserProvider.Dtos;

namespace UserProvider.Providers.MessageBroker.RabbitMq.Messages
{
    public class UserUpdateAnalyticsMessage : IntegrationMessage
    {
        public UserUpdateAnalytics Dto;

        public UserUpdateAnalyticsMessage(UserUpdateAnalytics dto)
        {
            Dto = dto;
        }
    }
}
