using Comment.Domain.Exceptions;
using Comment.Domain.Repositories;
using Comment.Domain.AggregatesModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Comment.Application.Dtos
{
    /// <summary>
    /// Public comment boundary. Every nested value is owned by the Application
    /// contract so HTTP serialization cannot accidentally expose an aggregate or
    /// a newly-added domain property. Moderation report counts are intentionally
    /// absent from this graph.
    /// </summary>
    public sealed class CommentCursorItemDto
    {
        public int Id { get; init; }
        public DateTime CreatedDate { get; init; }
        public string? Description { get; init; }
        public string? GifUrl { get; init; }
        public required string TargetId { get; init; }
        public required string TargetType { get; init; }
        public required CommentCursorUserDto User { get; init; }
        public required CommentCursorVisibilityDto VisibilityLevel { get; init; }
        public CommentCursorEngagementDto Engagement { get; init; } = new();
        public IReadOnlyList<CommentCursorCategoryDto> Categories { get; init; } = [];
        public CommentCursorInteractionStatusDto? InteractionStatus { get; set; }
        public IReadOnlyList<CommentCursorMediaDto> MediaItems { get; init; } = [];
        public IReadOnlyList<CommentCursorMentionDto> Mentions { get; init; } = [];
    }

    /// <summary>
    /// Synchronous write result. Engagement and actor status are deliberately
    /// absent because only the asynchronous Interaction projection can supply
    /// those values authoritatively.
    /// </summary>
    public sealed class CommentWriteResultDto
    {
        public int Id { get; init; }
        public DateTime CreatedDate { get; init; }
        public string? Description { get; init; }
        public string? GifUrl { get; init; }
        public required string TargetId { get; init; }
        public required string TargetType { get; init; }
        public required CommentCursorUserDto User { get; init; }
        public required CommentCursorVisibilityDto VisibilityLevel { get; init; }
        public IReadOnlyList<CommentCursorCategoryDto> Categories { get; init; } = [];
        public IReadOnlyList<CommentCursorMediaDto> MediaItems { get; init; } = [];
        public IReadOnlyList<CommentCursorMentionDto> Mentions { get; init; } = [];
    }

    public sealed record CommentCursorUserDto(int Id, string FullName);

    public sealed record CommentCursorVisibilityDto(int Id, string Name);

    public sealed class CommentCursorEngagementDto
    {
        public int TotalViews { get; set; }
        public int TotalRatings { get; set; }
        public int TotalLikes { get; set; }
        public int TotalDislikes { get; set; }
        public int TotalShares { get; set; }
        public int TotalComments { get; set; }
        public double RatingsTrueCount { get; set; }
        public double AverageRating =>
            TotalRatings == 0 ? 0 : RatingsTrueCount / TotalRatings;
        public long Version { get; set; }
    }

    public sealed record CommentCursorCategoryDto(string Name);

    public enum CommentCursorMediaTypeDto
    {
        Image = 1,
        Video = 2,
        Audio = 3,
    }

    public sealed record CommentCursorMediaDto(
        int Id,
        int Order,
        string Url,
        string FileName,
        CommentCursorMediaTypeDto MediaType,
        string? ThumbnailUrl,
        long? DurationSeconds,
        string MimeType,
        DateTime CreatedDate);

    public sealed record CommentCursorMentionDto(int UserId, string UserName);

    public static class CommentMediaAccessPath
    {
        public static string Create(int commentId, int mediaItemId)
        {
            if (commentId <= 0 || mediaItemId <= 0)
                throw new CommentException("A persisted comment media identity is required.");
            return $"/api/comments/{commentId}/media/{mediaItemId}";
        }
    }

    public sealed class CommentCursorInteractionStatusDto
    {
        public int CommentId { get; init; }
        public bool Liked { get; init; }
        public bool Disliked { get; init; }
        public bool Rated { get; init; }
    }

    public static class CommentCursorDtoMapper
    {
        public static CommentCursorItemDto ToDto(CommentCursorReadModel item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new CommentCursorItemDto
            {
                Id = item.Id,
                CreatedDate = item.CreatedDateUtc,
                Description = item.Description,
                GifUrl = item.GifUrl,
                TargetId = item.TargetId,
                TargetType = item.TargetType,
                User = new CommentCursorUserDto(
                    item.AuthorUserId,
                    item.AuthorDisplayName),
                VisibilityLevel = MapVisibility(item.VisibilityLevelId),
                Categories = (item.Categories ?? Array.Empty<CommentCategoryCursorReadModel>())
                    .Select(category => new CommentCursorCategoryDto(category.Name))
                    .ToArray(),
                MediaItems = (item.MediaItems ?? Array.Empty<CommentMediaCursorReadModel>())
                    .Select(media => new CommentCursorMediaDto(
                        media.Id,
                        media.Order,
                        CommentMediaAccessPath.Create(item.Id, media.Id),
                        media.FileName,
                        MapMediaType(media.MediaType),
                        null,
                        media.DurationSeconds,
                        media.MimeType,
                        media.CreatedDateUtc))
                    .ToArray(),
                Mentions = (item.Mentions ?? Array.Empty<CommentMentionCursorReadModel>())
                    .Select(mention => new CommentCursorMentionDto(
                        mention.UserId,
                        mention.UserName))
                    .ToArray(),
            };
        }

        public static IReadOnlyList<CommentCursorItemDto> ToDtos(
            IEnumerable<CommentCursorReadModel>? items) =>
            items?.Select(ToDto).ToArray() ?? Array.Empty<CommentCursorItemDto>();

        public static CommentCursorItemDto ToDto(CommentPost item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new CommentCursorItemDto
            {
                Id = item.Id,
                CreatedDate = item.CreatedDate,
                Description = item.Description,
                GifUrl = item.GifUrl,
                TargetId = item.TargetId,
                TargetType = item.TargetType,
                User = new CommentCursorUserDto(item.User.Id, item.User.FullName),
                VisibilityLevel = MapVisibility(item.VisibilityLevelId),
                Categories = item.Categories?
                    .Select(category => new CommentCursorCategoryDto(category.Name))
                    .ToArray() ?? [],
                MediaItems = item.MediaItems?
                    .Select(media => new CommentCursorMediaDto(
                        media.Id,
                        media.Order,
                        CommentMediaAccessPath.Create(item.Id, media.Id),
                        media.FileName,
                        MapMediaType(media.MediaType),
                        null,
                        media.DurationSeconds,
                        media.MimeType,
                        media.CreatedDate))
                    .ToArray() ?? [],
                Mentions = item.Mentions?
                    .Select(mention => new CommentCursorMentionDto(
                        mention.UserId,
                        mention.UserName))
                    .ToArray() ?? [],
            };
        }

        internal static CommentCursorVisibilityDto MapVisibility(int visibilityLevelId)
        {
            var visibility = VisibilityLevel.From(visibilityLevelId);
            if (visibility is null)
                throw new CommentException(
                    $"Persisted visibility level '{visibilityLevelId}' is invalid.");

            return new CommentCursorVisibilityDto(visibility.Id, visibility.Name);
        }

        internal static CommentCursorMediaTypeDto MapMediaType(
            StarterEntities.Storage.MediaType mediaType) => mediaType switch
            {
                StarterEntities.Storage.MediaType.Image => CommentCursorMediaTypeDto.Image,
                StarterEntities.Storage.MediaType.Video => CommentCursorMediaTypeDto.Video,
                StarterEntities.Storage.MediaType.Audio => CommentCursorMediaTypeDto.Audio,
                _ => throw new CommentException("The persisted comment media type is invalid."),
            };
    }

    public static class CommentWriteResultDtoMapper
    {
        public static CommentWriteResultDto ToDto(CommentPost item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new CommentWriteResultDto
            {
                Id = item.Id,
                CreatedDate = item.CreatedDate,
                Description = item.Description,
                GifUrl = item.GifUrl,
                TargetId = item.TargetId,
                TargetType = item.TargetType,
                User = new CommentCursorUserDto(item.User.Id, item.User.FullName),
                VisibilityLevel = CommentCursorDtoMapper.MapVisibility(
                    item.VisibilityLevelId),
                Categories = item.Categories?
                    .Select(category => new CommentCursorCategoryDto(category.Name))
                    .ToArray() ?? [],
                MediaItems = item.MediaItems?
                    .Select(media => new CommentCursorMediaDto(
                        media.Id,
                        media.Order,
                        CommentMediaAccessPath.Create(item.Id, media.Id),
                        media.FileName,
                        CommentCursorDtoMapper.MapMediaType(media.MediaType),
                        null,
                        media.DurationSeconds,
                        media.MimeType,
                        media.CreatedDate))
                    .ToArray() ?? [],
                Mentions = item.Mentions?
                    .Select(mention => new CommentCursorMentionDto(
                        mention.UserId,
                        mention.UserName))
                    .ToArray() ?? [],
            };
        }
    }
}
