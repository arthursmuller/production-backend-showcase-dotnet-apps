using MessageBrokerCore.Constants;
using MessageBrokerRabbitMq;
using Posts.Provider.Abstractions;
using Posts.Provider.Dtos;
using StarterEntities;

namespace Posts.Provider.Providers.RabbitMq
{
    public class PostsRabbitMqProvider : MessageBrokerRabbitMQPublisher, IBroker
    {
        public PostsRabbitMqProvider(
            IServiceProvider services,
            int retryCount) : base(services, retryCount) { }

        public Guid ShareCreated(UpdateCommentCountDto dto, string postType) =>
            Publish(
                new ShareCreatedMessage(dto),
                $"{postType}.share.created",
                Topology.Posts.EventsExchange);

        public Guid BookmarkCreated(BookmarkChangedDto dto, string postType) =>
            Publish(new BookmarkChangedMessage(dto),
                $"{postType}.bookmark.created",
                Topology.Posts.EventsExchange);

        public Guid CommentCreated(UpdateCommentCountDto dto, string postType) =>
            Publish(
                new CommentCreatedMessage(dto),
                $"{postType}.comment.created",
                Topology.Posts.EventsExchange);
    }
}