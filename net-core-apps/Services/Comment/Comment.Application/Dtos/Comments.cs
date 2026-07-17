using Comment.Domain.AggregatesModel;
using StarterEntities;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Comment.Application.Dtos
{
    public class CommentsCriteria : CommomFilter
    {
        public int[] Ids { get; set; }
        public int? UserId { get; set; }
        public CommentExternalProps[] RelatedEntityProps { get; set; }
    }

    public class CommentExternalProps
    {
        public string Id { get; set; }
        public string EntityType { get; set; }
    }

    public class AddCommentPostDto
    {
        public string Description { get; set; }
        public string IdOfRelatedEntity { get; set; }
        public string TypeOfRelatedEntity { get; set; }
        public string GifUrl { get; set; }
        public VisibilityLevelRecord VisibilityLevel { get; set; }
        public List<CategoryName> Categories { get; set; }
        public List<MediaFileWrapperDto> MediaItems { get; set; } = new();
        public List<UserMention> Mentions { get; set; } = new();

        public Task<List<AddMediaItemDomainDto>> ResolveMediaItems(MediaUploadDelegate saveFile = null) =>
            MediaConversionHelper.ConvertAdds(MediaItems, saveFile);
    }

    public class UpdateCommentDto
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public string GifUrl { get; set; }
        public VisibilityLevelRecord VisibilityLevel { get; set; }
        public List<CategoryName> Categories { get; set; }
        public List<CategoryName> CategoriesToRemove { get; set; }
        public CommomUpdateDomainDto<MediaFileWrapperDto, UpdateMediaItemDomainDto> MediaItems { get; set; } = new();
        public List<UserMention> Mentions { get; set; } = new();

        public async Task<CommomUpdateDomainDto<AddMediaItemDomainDto, UpdateMediaItemDomainDto>> ResolveMediaItemsUpdate(
            MediaUploadDelegate saveFile) =>
            new()
            {
                ToUpdate = MediaItems?.ToUpdate,
                ToRemove = MediaItems?.ToRemove,
                ToAdd = await MediaConversionHelper.ConvertAdds(MediaItems?.ToAdd, saveFile),
            };
    }

    public class CommentPostDto
    {
        public int Id { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string GifUrl { get; set; }
        public string IdOfRelatedEntity { get; set; }
        public string TypeOfRelatedEntity { get; set; }
        public User User { get; set; }
        public VisibilityLevelRecord VisibilityLevel { get; set; }
        public CommentSocialAnalytics Social { get; set; }
        public List<CategoryName> Categories { get; set; }
        public CheckSocialInteractionsDto SocialInteractionsStatus { get; set; }
        public List<MediaItemDomainViewDto> MediaItems { get; set; } = new();
        public List<UserMention> Mentions { get; set; } = new();
    }

    public static class CommentConversor
    {
        public static CommentPostDto Convert(CommentPost item) => new()
        {
            Id = item.Id,
            CreatedDate = item.CreatedDate,
            Description = item.Description,
            GifUrl = item.GifUrl,
            IdOfRelatedEntity = item.IdOfRelatedEntity,
            TypeOfRelatedEntity = item.TypeOfRelatedEntity,
            User = item.User,
            Social = item.Social,
            Categories = item.Categories,
            VisibilityLevel = item.VisibilityLevel,
            MediaItems = item.MediaItems?
                .Select(e => new MediaItemDomainViewDto(
                    e.Id,
                    e.Order,
                    e.Url,
                    e.FileName,
                    e.MediaType,
                    e.ThumbnailUrl,
                    e.DurationSeconds,
                    e.MimeType,
                    e.CreatedDate))
                .ToList(),
            Mentions = item.Mentions?.ToList() ?? new(),
        };

        public static IEnumerable<CommentPostDto> Convert(IEnumerable<CommentPost> comments) =>
            comments?.Select(Convert);
    }
}
