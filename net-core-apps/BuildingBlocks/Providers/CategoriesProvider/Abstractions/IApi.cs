using System.Threading.Tasks;

namespace CategoriesProvider.Abstractions
{
    public interface ICategoriesApi
    {
        Task<int> GetIdByName(string name, System.Threading.CancellationToken cancellationToken);
    }
}
