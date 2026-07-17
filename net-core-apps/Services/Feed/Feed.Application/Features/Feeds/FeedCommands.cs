using Feed.Application.Abstractions;
using Feed.Application.MessageBrokers.Messages;
using Feed.Domain.AggregatesModel;
using System.Linq;
using System.Threading.Tasks;

namespace Feed.Application.Features.Feeds
{
    public record AddFeedItemsCommand(IFeedRepository repository, IFollowingConnections followings)
        : Command<AddFeedItemsMessage, bool>
    {
        public override async Task<bool> Handle(AddFeedItemsMessage message)
        {
            var dto = message.Dto;
            var connections = await followings.GetUserConnectionsAsync(dto.UserId);
            var post = await repository.GetPostAsync(dto.PostId);
            if (post is null)
                return false;

            if (connections is not null)
            {
                var userIds = connections.Followers.Union(connections.Friends).Distinct();
                var items = userIds.Select(id => new UserConnectionsFeed
                {
                    UserId = id,
                    Description = dto.Description,
                    EventTrigger = dto.EventTrigger,
                    Post = post
                });

                await repository.AddFeedItemsAsync(items);
            }

            return true;
        }
    }

    public record RemoveFeedItemsCommand(IFeedRepository repository) : Command<int, bool>
    {
        public override async Task<bool> Handle(int postId)
        {
            await repository.RemoveFeedItemsAsync(postId);
            return true;
        }
    }
}
