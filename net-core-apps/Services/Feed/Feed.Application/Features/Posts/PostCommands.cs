using Feed.Application.Abstractions;
using Feed.Application.Dtos;
using Feed.Application.MessageBrokers;
using Feed.Domain.AggregatesModel;
using System.Threading.Tasks;

namespace Feed.Application.Features.Posts
{
    public record AddPostCommand(IFeedRepository repository, IMessages messages)
        : Command<UserConnectionsFeedDto, bool>
    {
        public override async Task<bool> Handle(UserConnectionsFeedDto dto)
        {
            var post = await repository.FindPostAsync(dto.EntityId, dto.EntityType, dto.UserId);
            if (post is null)
            {
                post = new Post(dto.CreatedDate)
                {
                    IsPublicViewable = true,
                    EntityId = dto.EntityId,
                    UserId = dto.UserId,
                    Type = dto.EntityType,
                };

                await repository.AddPostAsync(post);
            }

            messages.AddFeedItems(new AddFeedItemsDto(dto.UserId, post.Id, dto.Description, dto.EventTrigger));
            return true;
        }
    }

    public record RemovePostCommand(IFeedRepository repository, IMessages messages)
        : Command<RemovePostDto, bool>
    {
        public override async Task<bool> Handle(RemovePostDto dto)
        {
            var post = await repository.FindPostAsync(dto.EntityId, dto.EntityType);
            if (post is null)
                return false;

            await repository.RemovePostAsync(post);
            messages.RemoveFeedItems(post.Id);
            return true;
        }
    }

    public record ChangePostVisibilityCommand(IFeedRepository repository)
        : Command<ChangePostVisibilityDto, bool>
    {
        public override async Task<bool> Handle(ChangePostVisibilityDto dto)
        {
            var post = await repository.FindPostAsync(dto.EntityId, dto.EntityType);
            if (post is null)
                return false;

            post.IsPublicViewable = dto.IsPublic;
            await repository.SaveChangesAsync();
            return true;
        }
    }
}
