using CategoriesProvider.Abstractions;
using CategoriesProvider.Dtos;
using Following.Application.Abstractions;
using System.Threading.Tasks;

namespace Following.Infra.Providers
{
    public sealed class CategoryAdapter(ICategoriesService categories) : ICategories
    {
        public Task<int> GetIdByName(string name) => categories.Api.GetIdByName(name);

        public void UpdateFollow(int[] categoryIds, string[] categoryNames, bool isFollowing) =>
            categories.Broker.UpdateFollow(
                new UpdateCategoryFollowsDto(categoryIds, categoryNames, isFollowing));
    }
}
