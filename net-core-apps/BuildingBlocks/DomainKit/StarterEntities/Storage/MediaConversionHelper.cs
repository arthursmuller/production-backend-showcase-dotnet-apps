using Microsoft.AspNetCore.Http;
using StarterEntities.Storage;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public static class MediaConversionHelper
{
    public static async Task<List<AddMediaItemDomainDto>> ConvertAdds(
        IEnumerable<MediaFileWrapperDto> mediaItems,
        MediaUploadDelegate saveFile)
    {
        if (mediaItems == null || saveFile == null) return null;
        var arr = mediaItems.Where(e => e?.File != null).ToList();
        if (arr.Count == 0) return new List<AddMediaItemDomainDto>();

        var tasks = arr.Select(async (mediaFile, index) =>
        {
            var result = await saveFile(mediaFile.File);
            return new AddMediaItemDomainDto(
                index,
                result?.Url,
                result?.FileName ?? mediaFile.File?.FileName,
                result?.MediaType ?? mediaFile.MediaType,
                result?.ThumbnailUrl,
                result?.DurationSeconds,
                result?.MimeType
            );
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>
    /// Wraps an upload method that returns just a URL string into a MediaUploadDelegate.
    /// Useful so domains can keep their existing string-returning IStorage methods
    /// while still feeding the MediaConversionHelper.
    /// </summary>
    public static MediaUploadDelegate FromStringUploader(System.Func<IFormFile, Task<string>> stringUploader)
    {
        return async (IFormFile file) =>
        {
            if (file == null) return null;
            var url = await stringUploader(file);
            var mimeType = file.ContentType;
            return new MediaUploadResultDto
            {
                Url = url,
                FileName = file.FileName,
                MimeType = mimeType,
                MediaType = ResolveMediaType(mimeType),
                ThumbnailUrl = null,
                DurationSeconds = null,
            };
        };
    }

    public static MediaType ResolveMediaType(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return MediaType.Image;
        var lower = mimeType.ToLowerInvariant();
        if (lower.StartsWith("video/")) return MediaType.Video;
        if (lower.StartsWith("audio/")) return MediaType.Audio;
        return MediaType.Image;
    }
}

public class MediaFileWrapperDto
{
    public IFormFile File { get; set; }
    public MediaType MediaType { get; set; }
}

public delegate Task<MediaUploadResultDto> MediaUploadDelegate(IFormFile file);
