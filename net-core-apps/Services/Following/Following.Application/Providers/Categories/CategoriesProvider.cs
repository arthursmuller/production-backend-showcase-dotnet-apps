using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Providers.Categories
{
    public interface ICategories
    {
        Task<int> GetIdByName(string name, CancellationToken cancellationToken);
        Task UpdateFollowAsync(
            int[] categoryIds,
            string[] categoryNames,
            bool isFollowing,
            CancellationToken cancellationToken);
    }

    public sealed class CategoriesProvider(ICategories categories)
    {
        public Task<int> GetIdByName(string name, CancellationToken cancellationToken) =>
            categories.GetIdByName(name, cancellationToken);

        public Task UpdateFollowAsync(
            int[] categoryIds,
            string[] categoryNames,
            bool isFollowing,
            CancellationToken cancellationToken) =>
            categories.UpdateFollowAsync(categoryIds, categoryNames, isFollowing, cancellationToken);
    }
}
