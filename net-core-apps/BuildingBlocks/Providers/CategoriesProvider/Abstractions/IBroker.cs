using CategoriesProvider.Dtos;
using CategoriesProvider.Providers.RabbitMq.Messages;
using System;

namespace CategoriesProvider.Abstractions
{
    public interface IBroker
    {
        Guid UpdateFollow(UpdateCategoryFollowsDto dto);
        Guid? UpdateMentions(UpdateCategoryMentionsDto dto);
    }
}
