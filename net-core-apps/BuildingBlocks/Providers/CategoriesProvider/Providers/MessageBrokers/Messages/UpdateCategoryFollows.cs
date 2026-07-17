using CategoriesProvider.Dtos;
using MessageBrokerCore.Messages;

namespace CategoriesProvider.Providers.RabbitMq.Messages
{
    public class UpdateCategoryFollowsMessage : IntegrationMessage
    {
        public UpdateCategoryFollowsDto Dto { get; set; }
        public UpdateCategoryFollowsMessage(UpdateCategoryFollowsDto dto)
        {
            Dto = dto;
        }
    }
}
