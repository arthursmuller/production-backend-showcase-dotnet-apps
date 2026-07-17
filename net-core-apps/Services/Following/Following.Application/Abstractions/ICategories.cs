using System.Threading.Tasks;

namespace Following.Application.Abstractions
{
    public interface ICategories
    {
        Task<int> GetIdByName(string name);
        void UpdateFollow(int[] categoryIds, string[] categoryNames, bool isFollowing);
    }
}
