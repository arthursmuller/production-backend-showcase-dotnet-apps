using CategoriesProvider.Dtos;
using MessageBrokerCore.Messages;

namespace CategoriesProvider.Providers.RabbitMq.Messages
{
    public class UpdateCategoryMentionsMessage : IntegrationMessage
    {
        public UpdateCategoryMentionsDto Dto { get; set; }
        public UpdateCategoryMentionsMessage(UpdateCategoryMentionsDto dto)
        {
            Dto = dto;
        }
    }
}
