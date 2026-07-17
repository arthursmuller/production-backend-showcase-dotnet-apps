using FollowingProvider.Dtos;
using System.Threading.Tasks;

namespace FollowingProvider.Abstractions
{
    public interface IFollowingApi
    {
        Task<GetUserFollowersAndFriendsIdDto> GetUserFollowersAndFriendsIds(int userId);
    }
}
