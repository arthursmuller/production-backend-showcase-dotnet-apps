using System.Threading.Tasks;

namespace Feed.Application.Abstractions
{
    public record UserConnections(int[] Followers, int[] Friends);

    public interface IFollowingConnections
    {
        Task<UserConnections> GetUserConnectionsAsync(int userId);
    }
}
