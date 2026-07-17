using CategoriesProvider.Abstractions;
using CategoriesProvider.Dtos;
using Comment.Application.Abstractions;

namespace Comment.Infra.Providers
{
    public sealed class CategoryAdapter(ICategoriesService categories) : ICategories
    {
        public void UpdateMentions(string[] namesToAdd, string[] namesToRemove) =>
            categories.Broker.UpdateMentions(
                new UpdateCategoryMentionsDto(namesToAdd, namesToRemove));
    }
}
