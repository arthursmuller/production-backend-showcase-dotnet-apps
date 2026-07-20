using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace StarterEntities.Storage
{
    public enum MediaType
    {
        [Display(Name = "Image")]
        [Description("Image file")]
        Image = 1,

        [Display(Name = "Video")]
        [Description("Video file")]
        Video = 2,

        [Display(Name = "Audio")]
        [Description("Audio file")]
        Audio = 3,
    }

    /// <summary>
    /// Describes an incoming media stream without coupling application code to ASP.NET form files.
    /// </summary>
    public sealed record IncomingMediaFile(
        string FileName,
        string MimeType,
        long SizeBytes,
        MediaType MediaType,
        Func<Stream> OpenReadStream);

    public sealed class UpdateMediaItemDomainDto
    {
        public int Id { get; set; }
        public int? Order { get; set; }

        public UpdateMediaItemDomainDto() { }
        public UpdateMediaItemDomainDto(int id, int? order) => (Id, Order) = (id, order);
    }

    public sealed class MediaItemDomainViewDto
    {
        public int Id { get; set; }
        public int Order { get; set; }
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public MediaType MediaType { get; set; }
        public string? ThumbnailUrl { get; set; }
        public long? DurationSeconds { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }

        public MediaItemDomainViewDto() { }

        public MediaItemDomainViewDto(
            int id,
            int order,
            string url,
            string fileName,
            MediaType mediaType,
            string? thumbnailUrl,
            long? durationSeconds,
            string mimeType,
            DateTime createdDate) =>
            (Id, Order, Url, FileName, MediaType, ThumbnailUrl, DurationSeconds, MimeType, CreatedDate) =
            (id, order, url, fileName, mediaType, thumbnailUrl, durationSeconds, mimeType, createdDate);
    }
}
