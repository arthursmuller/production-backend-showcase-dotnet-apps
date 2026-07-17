using RichEnumeration;
using System;
using System.Collections.Generic;
using System.Linq;
using StarterEntities.Storage;

namespace StarterEntities
{
    public class PlatformContexts : Enumeration
    {
        public string Description { get; set; }

        public static PlatformContexts Comment = new PlatformContexts(18, nameof(Comment), "Comentário");

        public PlatformContexts(int id, string name, string description) : base(id, name)
        {
            Description = description;
        }
        public PlatformContexts(int id, string name) : base(id, name) { }
    }
    public record User(int Id, string FullName)
    {
        public static explicit operator string(User user) => user.FullName;
        public static explicit operator int(User user) => user.Id;
    }
    public record CategoryName(string Name)
    {
        public static explicit operator string(CategoryName value) => value.Name;
    }
    public record CategoryRecord(int Id, string Name)
    {
        public static explicit operator string(CategoryRecord value) => value.Name;
        public static explicit operator int(CategoryRecord value) => value.Id;
    }
    public class CommomUpdateDomainDto<TAdd, TUpdate>
    {
        public List<TAdd> ToAdd { get; set; }
        public List<TUpdate> ToUpdate { get; set; }
        public int[] ToRemove { get; set; }
    }

    public static class CommomUpdateDomainBehaviorWorker
    {
        public static void HandleAddUpdateDelete<TAdd, TUpdate, TEntity>(
            CommomUpdateDomainDto<TAdd, TUpdate> items,
            List<TEntity> entities,
            Func<TAdd, TEntity> add,
            Action<TUpdate, TEntity> update,
            Func<TUpdate, TEntity, bool> updateMatch,
            Predicate<TEntity> delete)
        {
            if (items == null || entities == null) return;

            if (items.ToUpdate is not null && update is not null && updateMatch is not null)
            {
                foreach (var toUpdate in items.ToUpdate)
                {
                    var entity = entities.FirstOrDefault(candidate => updateMatch(toUpdate, candidate));
                    if (entity is not null)
                        update(toUpdate, entity);
                }
            }

            if (items.ToAdd is not null && add is not null)
            {
                foreach (var toAdd in items.ToAdd)
                {
                    var newEntity = add(toAdd);
                    if (newEntity is not null)
                        entities.Add(newEntity);
                }
            }

            if (items.ToRemove is not null && delete is not null)
            {
                entities.RemoveAll(delete);
            }
        }

    }

    public class CommomMediaItem : BaseEntity
    {
        public int Order { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public MediaType MediaType { get; set; }
        public string ThumbnailUrl { get; set; }
        public long? DurationSeconds { get; set; }
        public string MimeType { get; set; }

        public CommomMediaItem() { }

        public CommomMediaItem(int order, string url, string fileName, MediaType mediaType, string thumbnailUrl = null, long? durationSeconds = null, string mimeType = null)
        {
            Order = order;
            Url = url;
            FileName = fileName;
            MediaType = mediaType;
            ThumbnailUrl = thumbnailUrl;
            DurationSeconds = durationSeconds;
            MimeType = mimeType;
        }

        public override bool Equals(object obj)
        {
            if (obj is CommomMediaItem other)
                return Url == other.Url && FileName == other.FileName && MediaType == other.MediaType;
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Url, FileName, MediaType);
        }
    }

    /// <summary>
    /// Reusable helper that mutates a list of media items based on a CommomUpdateDomainDto:
    /// - Updates Order on items matched by Id (ToUpdate)
    /// - Adds new items via the provided factory (ToAdd)
    /// - Removes items by Id (ToRemove)
    /// Used by retained entities that own media collections.
    /// </summary>
    public static class MediaItemsHelper
    {
        public static void SetMediaItems<TItem>(
            List<TItem> mediaItems,
            CommomUpdateDomainDto<AddMediaItemDomainDto, UpdateMediaItemDomainDto> items,
            Func<AddMediaItemDomainDto, TItem> factory)
            where TItem : CommomMediaItem
        {
            CommomUpdateDomainBehaviorWorker.HandleAddUpdateDelete(
                items,
                mediaItems,
                add: toAdd => factory(toAdd),
                update: (toUpdate, entity) => { entity.Order = toUpdate.Order ?? entity.Order; },
                updateMatch: (toUpdate, entity) => toUpdate.Id == entity.Id,
                delete: entity => items.ToRemove.Contains(entity.Id));
        }

        public static void AddMediaItems<TItem>(
            List<TItem> mediaItems,
            IEnumerable<AddMediaItemDomainDto> items,
            Func<AddMediaItemDomainDto, TItem> factory)
            where TItem : CommomMediaItem
        {
            if (items == null || mediaItems == null) return;
            foreach (var item in items)
                mediaItems.Add(factory(item));
        }
    }

}
