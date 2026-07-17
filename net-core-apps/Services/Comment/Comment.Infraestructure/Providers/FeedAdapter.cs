using Comment.Application.Abstractions;
using FeedProvider.Abstractions;

namespace Comment.Infra.Providers
{
    public sealed class FeedAdapter(IFeedService feed) : IFeed
    {
        public void Remove(string entityType, string entityId) =>
            feed.Broker.Remove(entityType, entityId);
    }
}
