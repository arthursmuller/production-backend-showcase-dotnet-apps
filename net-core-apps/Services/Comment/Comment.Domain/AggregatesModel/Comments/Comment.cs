using StarterEntities;
using StarterEntities.Storage;
using Comment.Domain.Events;
using System.Collections.Generic;

namespace Comment.Domain.AggregatesModel
{
    public class CommentPost : BaseEventingEntity, AnalyticalEntity<CommentSocialAnalytics>
    {
        public User User { get; set; }
        public string RelatedEntity => _relatedEntity;
        private string _relatedEntity;
        public string Description{ get; set; }
        public string GifUrl{ get; set; }
        public string IdOfRelatedEntity{ get; set; }
        public string TypeOfRelatedEntity { get; set; }
        public bool PublishedOnFeed { get; set; }

        public VisibilityLevelRecord VisibilityLevel { get; set; }
        public CommentSocialAnalytics Social { get; set; }
        public List<CategoryName> Categories { get; set; }
        public virtual List<CommentMediaItem> MediaItems { get; set; } = new();
        public virtual List<UserMention> Mentions { get; set; } = new();

        public CommentPost() { }

        public CommentPost(
            User user,
            string description,
            string gifUrl,
            string idOfRelatedEntity,
            string typeOfRelatedEntity,
            List<CategoryName> categories,
            VisibilityLevelRecord visibilityLevel,
            IEnumerable<AddMediaItemDomainDto> mediaItems,
            IEnumerable<UserMention> mentions)
        {
            User = user;
            Description = description;
            GifUrl = gifUrl;
            IdOfRelatedEntity = idOfRelatedEntity;
            TypeOfRelatedEntity = typeOfRelatedEntity;
            Categories = categories;
            VisibilityLevel = visibilityLevel
                ?? new(StarterEntities.VisibilityLevel.Private.Id, StarterEntities.VisibilityLevel.Private.Description);
            Social = new();

            MediaItemsHelper.AddMediaItems(MediaItems, mediaItems, ConfigurationMediaItemFactory);
            SetMentions(mentions, Description);

            RaiseEvent(new CommentAddedEvent(this));
        }

        public void Update(
            string description,
            string gifUrl,
            VisibilityLevelRecord visibilityLevel,
            List<CategoryName> categories,
            List<CategoryName> categoriesToRemove,
            CommomUpdateDomainDto<AddMediaItemDomainDto, UpdateMediaItemDomainDto> mediaItems,
            IEnumerable<UserMention> mentions)
        {
            var previousVisibilityLevel = VisibilityLevel;

            Description = description;
            GifUrl = gifUrl;
            VisibilityLevel = visibilityLevel ?? VisibilityLevel;

            Categories.RemoveAll(e => categoriesToRemove?.Contains(e) ?? false);
            Categories.AddRange(categories ?? new());

            SetMediaItems(mediaItems);
            SetMentions(mentions, Description);

            RaiseEvent(new CommentUpdatedEvent(this, previousVisibilityLevel));
        }

        public void SetMediaItems(CommomUpdateDomainDto<AddMediaItemDomainDto, UpdateMediaItemDomainDto> items)
        {
            MediaItems ??= new();
            MediaItemsHelper.SetMediaItems(MediaItems, items, ConfigurationMediaItemFactory);
        }

        public void SetMentions(IEnumerable<UserMention> incoming, string description)
            => Mentions = UserMentionsHelper.Sanitize(description, incoming);

        public static CommentMediaItem ConfigurationMediaItemFactory(AddMediaItemDomainDto e)
            => new CommentMediaItem(e.Order, e.Url, e.FileName, e.MediaType, e.ThumbnailUrl, e.DurationSeconds, e.MimeType);
    }

    public class CommentMediaItem : CommomMediaItem
    {
        public CommentMediaItem() {}
        public CommentMediaItem(int order, string url, string fileName, MediaType mediaType, string thumbnailUrl = null, long? durationSeconds = null, string mimeType = null)
            : base(order, url, fileName, mediaType, thumbnailUrl, durationSeconds, mimeType) {}
    }
}
