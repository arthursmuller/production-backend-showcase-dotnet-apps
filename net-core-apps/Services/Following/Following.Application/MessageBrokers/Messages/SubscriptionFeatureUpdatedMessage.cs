using Following.Application.Dtos;
using MessageBrokerCore.Messages;

namespace Following.Application.MessageBrokers.Messages
{
    public class SubscriptionFeatureUpdatedMessage : IntegrationMessage
    {
        public SubscriptionFeatureUpdatedDto Dto { get; set; }

        public SubscriptionFeatureUpdatedMessage(SubscriptionFeatureUpdatedDto dto)
        {
            Dto = dto;
        }

        public SubscriptionFeatureUpdatedMessage() { }
    }
}
