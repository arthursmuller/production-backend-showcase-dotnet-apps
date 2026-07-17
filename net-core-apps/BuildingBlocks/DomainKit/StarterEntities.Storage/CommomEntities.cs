using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

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
        [Description("Audio")]
        Audio = 3
    }

    public class MediaUploadResultDto
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public MediaType MediaType { get; set; }
        public string ThumbnailUrl { get; set; }
        public long? DurationSeconds { get; set; }
        public string MimeType { get; set; }
    }

    public class AddMediaItemDomainDto
    {
        public int Order { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public MediaType MediaType { get; set; }
        public string ThumbnailUrl { get; set; }
        public long? DurationSeconds { get; set; }
        public string MimeType { get; set; }

        public AddMediaItemDomainDto(
            int order,
            string url,
            string fileName,
            MediaType mediaType,
            string thumbnailUrl,
            long? durationSeconds,
            string mimeType)
        {
            Order = order;
            Url = url;
            FileName = fileName;
            MediaType = mediaType;
            ThumbnailUrl = thumbnailUrl;
            DurationSeconds = durationSeconds;
            MimeType = mimeType;
        }
    }

    public class UpdateMediaItemDomainDto
    {
        public int Id { get; set; }
        public int? Order { get; set; }
    }

    public class MediaItemDomainViewDto
    {
        public int Id { get; set; }
        public int Order { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public MediaType MediaType { get; set; }
        public string ThumbnailUrl { get; set; }
        public long? DurationSeconds { get; set; }
        public string MimeType { get; set; }
        public DateTime CreatedDate { get; set; }

        public MediaItemDomainViewDto() { }

        public MediaItemDomainViewDto(
            int id,
            int order,
            string url,
            string fileName,
            MediaType mediaType,
            string thumbnailUrl,
            long? durationSeconds,
            string mimeType,
            DateTime createdDate)
        {
            Id = id;
            Order = order;
            Url = url;
            FileName = fileName;
            MediaType = mediaType;
            ThumbnailUrl = thumbnailUrl;
            DurationSeconds = durationSeconds;
            MimeType = mimeType;
            CreatedDate = createdDate;
        }
    }
}
