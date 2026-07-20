using Comment.Domain.Exceptions;
using StarterEntities;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;

namespace Comment.Domain.AggregatesModel
{
    public static class CommentMediaRules
    {
        public const long MaxImageBytes = 10L * 1024 * 1024;
        public const long MaxVideoBytes = 30L * 1024 * 1024;
        public const long MaxAudioBytes = 20L * 1024 * 1024;

        public static void EnsureValid(string fileName, string mimeType, long sizeBytes, MediaType mediaType)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new CommentException("A media file name is required.");
            if (sizeBytes <= 0)
                throw new CommentException($"Media file '{fileName}' is empty.");
            if (string.IsNullOrWhiteSpace(mimeType))
                throw new CommentException($"Media file '{fileName}' has no content type.");

            var allowedMimeTypes = mediaType switch
            {
                MediaType.Image => new[] { "image/jpeg", "image/png", "image/gif", "image/webp" },
                MediaType.Video => new[] { "video/mp4" },
                MediaType.Audio => new[] { "audio/mpeg", "audio/wav", "audio/ogg" },
                _ => throw new CommentException($"Media file '{fileName}' has an unsupported media type."),
            };

            var maxBytes = mediaType switch
            {
                MediaType.Image => MaxImageBytes,
                MediaType.Video => MaxVideoBytes,
                MediaType.Audio => MaxAudioBytes,
                _ => 0,
            };

            if (!string.Equals(mimeType, mimeType.Trim(), StringComparison.Ordinal)
                || !Array.Exists(
                    allowedMimeTypes,
                    allowed => allowed.Equals(mimeType, StringComparison.OrdinalIgnoreCase)))
                throw new CommentException($"Media file '{fileName}' does not match its declared media type.");
            if (sizeBytes > maxBytes)
                throw new CommentException($"Media file '{fileName}' exceeds the {maxBytes / (1024 * 1024)} MiB limit.");
        }
    }

    public sealed record NewCommentMedia(
        int Order,
        string Url,
        string FileName,
        MediaType MediaType,
        string? ThumbnailUrl,
        long? DurationSeconds,
        string MimeType,
        long SizeBytes);

    public class CommentMediaItem : BaseEntity
    {
        public int Order { get; private set; }
        public string Url { get; private set; } = string.Empty;
        public string FileName { get; private set; } = string.Empty;
        public MediaType MediaType { get; private set; }
        public string? ThumbnailUrl { get; private set; }
        public long? DurationSeconds { get; private set; }
        public string MimeType { get; private set; } = string.Empty;

        private CommentMediaItem() { }

        internal CommentMediaItem(NewCommentMedia media)
        {
            CommentMediaRules.EnsureValid(media.FileName, media.MimeType, media.SizeBytes, media.MediaType);
            if (string.IsNullOrWhiteSpace(media.Url))
                throw new CommentException($"Media file '{media.FileName}' has no storage URL.");
            if (media.Order < 0)
                throw new CommentException("Media display order cannot be negative.");
            if (media.Url.Trim().Length > CommentFieldLimits.Url)
                throw new CommentException($"Media URLs cannot exceed {CommentFieldLimits.Url} characters.");
            if (media.FileName.Trim().Length > 255)
                throw new CommentException("Media file names cannot exceed 255 characters.");
            if (media.MimeType.Trim().Length > 100)
                throw new CommentException("Media MIME types cannot exceed 100 characters.");
            if (!string.IsNullOrWhiteSpace(media.ThumbnailUrl) &&
                media.ThumbnailUrl.Trim().Length > CommentFieldLimits.Url)
                throw new CommentException($"Media thumbnail URLs cannot exceed {CommentFieldLimits.Url} characters.");
            if (media.DurationSeconds < 0)
                throw new CommentException("Media duration cannot be negative.");

            Order = media.Order;
            Url = media.Url.Trim();
            FileName = media.FileName.Trim();
            MediaType = media.MediaType;
            ThumbnailUrl = string.IsNullOrWhiteSpace(media.ThumbnailUrl)
                ? null
                : media.ThumbnailUrl.Trim();
            DurationSeconds = media.DurationSeconds;
            MimeType = media.MimeType.Trim();
        }

        internal void Reorder(int? order)
        {
            if (order.HasValue)
            {
                if (order.Value < 0)
                    throw new CommentException("Media display order cannot be negative.");
                Order = order.Value;
            }
        }
    }
}
