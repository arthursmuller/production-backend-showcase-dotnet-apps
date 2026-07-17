using Feed.Application.Abstractions;
using FollowingProvider.Abstractions;
using System.Threading.Tasks;

namespace Feed.Infra.Providers
{
    public sealed class FollowingConnections(IFollowingService service) : IFollowingConnections
    {
        public async Task<UserConnections> GetUserConnectionsAsync(int userId)
        {
            var result = await service.Api.GetUserFollowersAndFriendsIds(userId);
            return result is null
                ? null
                : new UserConnections(result.Followers ?? [], result.Friends ?? []);
        }
    }
}
