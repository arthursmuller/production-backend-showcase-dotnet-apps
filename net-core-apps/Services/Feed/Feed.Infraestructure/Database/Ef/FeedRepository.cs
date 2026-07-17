using Feed.Application.Abstractions;
using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class FeedRepository(FeedContext context) : IFeedRepository
    {
        public Task<Post> FindPostAsync(string entityId, string entityType, int? userId = null)
        {
            var query = context.Posts.Where(post =>
                post.EntityId == entityId && post.Type == entityType);

            if (userId.HasValue)
                query = query.Where(post => post.UserId == userId.Value);

            return query.FirstOrDefaultAsync();
        }

        public Task<Post> GetPostAsync(int postId) =>
            context.Posts.FirstOrDefaultAsync(post => post.Id == postId);

        public async Task<Post> AddPostAsync(Post post)
        {
            context.Posts.Add(post);
            await context.SaveChangesAsync();
            return post;
        }

        public async Task RemovePostAsync(Post post)
        {
            if (post is null)
                return;

            context.Posts.Remove(post);
            await context.SaveChangesAsync();
        }

        public async Task AddFeedItemsAsync(IEnumerable<UserConnectionsFeed> items)
        {
            await context.UserConnectionsFeed.AddRangeAsync(items);
            await context.SaveChangesAsync();
        }

        public async Task RemoveFeedItemsAsync(int postId)
        {
            var items = context.UserConnectionsFeed.Where(item => item.Post.Id == postId);
            context.UserConnectionsFeed.RemoveRange(items);
            await context.SaveChangesAsync();
        }

        public async Task SaveChangesAsync() => await context.SaveChangesAsync();
    }
}
