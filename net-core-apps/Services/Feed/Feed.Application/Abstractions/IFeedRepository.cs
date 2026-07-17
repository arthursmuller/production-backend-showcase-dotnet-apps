using Feed.Domain.AggregatesModel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Feed.Application.Abstractions
{
    public interface IFeedRepository
    {
        Task<Post> FindPostAsync(string entityId, string entityType, int? userId = null);
        Task<Post> GetPostAsync(int postId);
        Task<Post> AddPostAsync(Post post);
        Task RemovePostAsync(Post post);
        Task AddFeedItemsAsync(IEnumerable<UserConnectionsFeed> items);
        Task RemoveFeedItemsAsync(int postId);
        Task SaveChangesAsync();
    }
}
