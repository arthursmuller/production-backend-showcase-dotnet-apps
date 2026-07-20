using Comment.Domain.Events;
using Comment.Domain.Exceptions;
using Comment.Domain.Dtos;
using DomainEventing;
using StarterEntities;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Comment.Domain.AggregatesModel
{
    public interface ICommentConcurrencyTracked
    {
        long Version { get; }
    }

    public static class CommentFieldLimits
    {
        public const int TargetType = 64;
        public const int TargetId = 256;
        public const int Description = 10_000;
        public const int Url = 2_048;
        public const int UserName = 200;
        public const int CategoryName = 100;
        public const int MentionUserName = 50;
    }

    public class CommentPost : DomainBaseEntity, ICommentConcurrencyTracked
    {
        public User User { get; private set; } = new(0, string.Empty);
        public int AuthorUserId { get; private set; }
        public string TargetId { get; private set; } = string.Empty;
        public string TargetType { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public string? GifUrl { get; private set; }
        public bool PublishedOnFeed { get; private set; }
        public bool CategoryMentionsPublished { get; private set; }
        public int VisibilityLevelId { get; private set; }
        public long Version { get; private set; } = 1;

        public List<CategoryName> Categories { get; private set; } = new();
        public virtual List<CommentMediaItem> MediaItems { get; private set; } = new();
        public virtual List<UserMention> Mentions { get; private set; } = new();

        private CommentPost() { }

        public CommentPost(
            User user,
            AddCommentDomainDto dto,
            IEnumerable<NewCommentMedia> mediaItems)
        {
            if (dto is null)
                throw new CommentException("Comment data is required.");
            if (user is null || user.Id <= 0)
                throw new CommentException("A valid comment author is required.");
            if (string.IsNullOrWhiteSpace(dto.TargetId))
                throw new CommentException("A related entity id is required.");
            if (string.IsNullOrWhiteSpace(dto.TargetType))
                throw new CommentException("A related entity type is required.");

            User = user;
            AuthorUserId = user.Id;
            Description = NormalizeOptional(
                dto.Description,
                CommentFieldLimits.Description,
                "Comment descriptions");
            GifUrl = NormalizeOptional(dto.GifUrl, CommentFieldLimits.Url, "GIF URLs");
            TargetId = NormalizeRequired(
                dto.TargetId,
                CommentFieldLimits.TargetId,
                "Related entity ids");
            TargetType = NormalizeRequired(
                dto.TargetType,
                CommentFieldLimits.TargetType,
                "Related entity types").ToLowerInvariant();
            Categories = NormalizeCategories(dto.Categories);
            VisibilityLevelId = CommentVisibilityRules.Normalize(dto.VisibilityLevelId);

            AddMediaItems(mediaItems);
            SetMentions(dto.Mentions, Description);
            var audiencePolicy = GetAudiencePolicy();

            if (CommentVisibilityRules.IsPublic(VisibilityLevelId))
            {
                RaisePostCountChanged(increment: true);
                CategoryMentionsPublished = true;
                RaiseCategoryMentionsChanged(
                    Categories.Select(category => category.Name),
                    Array.Empty<string>());
            }

            if (audiencePolicy != CommentAudiencePolicies.Owner)
            {
                PublishedOnFeed = true;
                RaiseEvent(new CommentFeedAddRequestedEvent(
                    this,
                    Version,
                    CreatedDate));
            }

            RaiseMentionNotifications(Mentions);
            RaiseInteractionStateChanged(isNew: true, deleted: false, Version, DateTime.UtcNow);
        }

        public void EnsureOwnedBy(int userId)
        {
            if (userId <= 0 || AuthorUserId != userId)
                throw new CommentException("Only the comment owner can modify this comment.");
        }

        public void Update(
            int actorUserId,
            UpdateCommentDomainDto dto)
        {
            if (dto is null)
                throw new CommentException("Comment update data is required.");

            EnsureOwnedBy(actorUserId);
            var previousAudiencePolicy = GetAudiencePolicy();
            var wasPublic = CommentVisibilityRules.IsPublic(VisibilityLevelId);
            var hadPublishedCategoryMentions = CategoryMentionsPublished;
            var previousCategoryNames = Categories
                .Select(category => category.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previousMentionUserIds = Mentions
                .Select(mention => mention.UserId)
                .ToHashSet();

            Description = NormalizeOptional(
                dto.Description,
                CommentFieldLimits.Description,
                "Comment descriptions");
            GifUrl = NormalizeOptional(dto.GifUrl, CommentFieldLimits.Url, "GIF URLs");
            if (dto.VisibilityLevelId.HasValue)
                VisibilityLevelId = CommentVisibilityRules.Normalize(dto.VisibilityLevelId);

            var namesToRemove = (dto.CategoriesToRemove ?? new List<CategoryName>())
                .Where(category => category is not null && !string.IsNullOrWhiteSpace(category.Name))
                .Select(category => category.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Categories.RemoveAll(category => namesToRemove.Contains(category.Name));

            foreach (var category in NormalizeCategories(dto.Categories))
            {
                if (!Categories.Any(existing => string.Equals(existing.Name, category.Name, StringComparison.OrdinalIgnoreCase)))
                    Categories.Add(category);
            }

            SetMediaItems(dto.MediaItems);
            SetMentions(dto.Mentions, Description);
            UpdateDate = DateTime.UtcNow;

            var audiencePolicy = GetAudiencePolicy();
            var isPublic = CommentVisibilityRules.IsPublic(VisibilityLevelId);
            var currentCategoryNames = Categories
                .Select(category => category.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previouslyCountedCategories = hadPublishedCategoryMentions
                ? previousCategoryNames
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentlyCountedCategories = isPublic
                ? currentCategoryNames
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            RaiseCategoryMentionsChanged(
                currentlyCountedCategories.Except(
                    previouslyCountedCategories,
                    StringComparer.OrdinalIgnoreCase),
                previouslyCountedCategories.Except(
                    currentlyCountedCategories,
                    StringComparer.OrdinalIgnoreCase));
            CategoryMentionsPublished = isPublic;

            if (wasPublic != isPublic)
                RaisePostCountChanged(increment: isPublic);

            if (!PublishedOnFeed && audiencePolicy != CommentAudiencePolicies.Owner)
            {
                PublishedOnFeed = true;
                RaiseEvent(new CommentFeedAddRequestedEvent(
                    this,
                    checked(Version + 1),
                    UpdateDate));
            }
            else if (PublishedOnFeed && !string.Equals(
                previousAudiencePolicy,
                audiencePolicy,
                StringComparison.Ordinal))
            {
                RaiseEvent(new CommentFeedVisibilityChangedEvent(
                    Id,
                    audiencePolicy,
                    AuthorUserId,
                    checked(Version + 1),
                    UpdateDate));
            }

            RaiseMentionNotifications(Mentions.Where(mention =>
                !previousMentionUserIds.Contains(mention.UserId)));
            RaiseInteractionStateChanged(isNew: false, deleted: false, checked(Version + 1), UpdateDate);
        }

        public void AttachMedia(IEnumerable<NewCommentMedia> mediaItems) =>
            AddMediaItems(mediaItems);

        public void PrepareForRemoval(int actorUserId)
        {
            EnsureOwnedBy(actorUserId);
            var removedAtUtc = DateTime.UtcNow;
            if (CommentVisibilityRules.IsPublic(VisibilityLevelId))
                RaisePostCountChanged(increment: false);

            if (PublishedOnFeed)
                RaiseEvent(new CommentFeedRemoveRequestedEvent(
                    Id,
                    AuthorUserId,
                    checked(Version + 1),
                    removedAtUtc));

            if (CategoryMentionsPublished)
            {
                RaiseCategoryMentionsChanged(
                    Array.Empty<string>(),
                    Categories.Select(category => category.Name));
                CategoryMentionsPublished = false;
            }

            RaiseInteractionStateChanged(isNew: false, deleted: true, checked(Version + 1), removedAtUtc);
        }

        private void AddMediaItems(IEnumerable<NewCommentMedia> items)
        {
            if (items is null)
                return;
            foreach (var item in items)
                MediaItems.Add(new CommentMediaItem(item));

            EnsureUniqueMediaOrder();
        }

        private void SetMediaItems(CommentMediaUpdateDomainDto changes)
        {
            if (changes is null)
                return;

            var updates = changes.ToUpdate?.ToDictionary(item => item.Id) ?? new Dictionary<int, UpdateMediaItemDomainDto>();
            foreach (var mediaItem in MediaItems)
            {
                if (updates.TryGetValue(mediaItem.Id, out var update))
                    mediaItem.Reorder(update.Order);
            }

            var removals = (changes.ToRemove ?? Array.Empty<int>()).ToHashSet();
            MediaItems.RemoveAll(item => removals.Contains(item.Id));
            EnsureUniqueMediaOrder();
        }

        private void SetMentions(IEnumerable<UserMention> incoming, string? description) =>
            Mentions = CommentMentionRules.Sanitize(description, incoming);

        private void RaisePostCountChanged(bool increment) =>
            RaiseEvent(new CommentPostCountChangedEvent(
                TargetId,
                increment,
                AuthorUserId,
                TargetType));

        private void RaiseCategoryMentionsChanged(
            IEnumerable<string> namesToAdd,
            IEnumerable<string> namesToRemove)
        {
            var toAdd = namesToAdd?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();
            var toRemove = namesToRemove?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();

            if (toAdd.Length > 0 || toRemove.Length > 0)
                RaiseEvent(new CommentCategoryMentionsChangedEvent(toAdd, toRemove, AuthorUserId));
        }

        private void RaiseMentionNotifications(IEnumerable<UserMention> mentions)
        {
            var audiencePolicy = GetAudiencePolicy();
            foreach (var mention in mentions ?? Array.Empty<UserMention>())
            {
                if (mention.UserId != AuthorUserId)
                    RaiseEvent(new CommentMentionNotificationRequestedEvent(
                        this,
                        mention.UserId,
                        audiencePolicy));
            }
        }

        public string GetAudiencePolicy() =>
            CommentAudiencePolicies.FromVisibilityIdOrOwner(VisibilityLevelId);

        private void RaiseInteractionStateChanged(
            bool isNew,
            bool deleted,
            long sourceVersion,
            DateTime occurredAtUtc) =>
            RaiseEvent(new CommentInteractionStateChangedEvent(
                this,
                isNew,
                deleted,
                sourceVersion,
                new DateTimeOffset(DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc))));

        private static List<CategoryName> NormalizeCategories(IEnumerable<CategoryName> categories) =>
            (categories ?? Array.Empty<CategoryName>())
                .Where(category => category is not null && !string.IsNullOrWhiteSpace(category.Name))
                .GroupBy(category => category.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new CategoryName(NormalizeRequired(
                    group.Key,
                    CommentFieldLimits.CategoryName,
                    "Category names")))
                .ToList();

        private void EnsureUniqueMediaOrder()
        {
            if (MediaItems.Any(item => item.Order < 0))
                throw new CommentException("Media display order cannot be negative.");
            if (MediaItems.GroupBy(item => item.Order).Any(group => group.Count() > 1))
                throw new CommentException("Media display order must be unique within a comment.");
        }

        private static string NormalizeRequired(string? value, int maximumLength, string fieldName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                throw new CommentException($"{fieldName} are required.");
            if (normalized.Length > maximumLength)
                throw new CommentException($"{fieldName} cannot exceed {maximumLength} characters.");
            return normalized;
        }

        private static string? NormalizeOptional(string? value, int maximumLength, string fieldName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrEmpty(normalized))
                return null;
            if (normalized.Length > maximumLength)
                throw new CommentException($"{fieldName} cannot exceed {maximumLength} characters.");
            return normalized;
        }
    }
}
