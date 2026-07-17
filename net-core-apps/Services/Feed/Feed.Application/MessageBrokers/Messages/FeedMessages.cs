using Feed.Application.Dtos;
using MessageBrokerCore.Messages;
using Newtonsoft.Json;

namespace Feed.Application.MessageBrokers.Messages
{
    public class AddFeedItemsMessage : IntegrationMessage
    {
        public AddFeedItemsDto Dto { get; set; }

        public AddFeedItemsMessage(AddFeedItemsDto dto) => Dto = dto;
        public AddFeedItemsMessage() { }
    }

    public class RemoveFeedItemsMessage : IntegrationMessage
    {
        [JsonProperty("UserId")]
        public int PostId { get; set; }

        public RemoveFeedItemsMessage(int postId) => PostId = postId;
        public RemoveFeedItemsMessage() { }
    }

    public class AddPostsMessage : IntegrationMessage
    {
        public UserConnectionsFeedDto Dto { get; set; }
    }

    public class ChangeVisibilityMessage : IntegrationMessage
    {
        public ChangePostVisibilityDto Dto { get; set; }
    }

    public class RemoveUserConnectionsMessage : IntegrationMessage
    {
        public RemovePostDto Dto { get; set; }
    }
}
