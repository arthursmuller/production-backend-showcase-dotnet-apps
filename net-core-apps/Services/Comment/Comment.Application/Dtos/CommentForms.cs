using Comment.Application.Dtos;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Dtos;
using Comment.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Comment.Application.Dtos
{
    /// <summary>
    /// Multipart-only media information. The domain sees a transport-neutral
    /// media description after this request has been mapped.
    /// </summary>
    public class MediaFileWrapperDto
    {
        public IFormFile? File { get; set; }
        public MediaType MediaType { get; set; }
    }

    public class AddCommentFormDto : AddCommentDomainDto
    { }

    /// <summary>
    /// Extends the domain update shape with the entity lookup id used by the
    /// application orchestrator.
    /// </summary>
    public class UpdateCommentFormDto
        : UpdateCommentDomainDto<CommentMediaUpdateFormDto>
    {
        public int Id { get; set; }
    }

    public class CommentMediaUpdateFormDto : CommentMediaUpdateDomainDto
    { }

    public static class CommentFormMapper
    {
        public static AddCommentPostDto ToApplication(
            this AddCommentFormDto form,
            IEnumerable<MediaFileWrapperDto>? files,
            string? mediaUploadIdentity = null) =>
            new()
            {
                Description = form.Description,
                TargetId = form.TargetId,
                TargetType = form.TargetType,
                GifUrl = form.GifUrl,
                VisibilityLevelId = form.VisibilityLevelId,
                Categories = form.Categories ?? new(),
                Mentions = form.Mentions ?? new(),
                MediaItems = ToIncomingFiles(files),
                MediaUploadIdentity = mediaUploadIdentity,
            };

        public static UpdateCommentDto ToApplication(
            this UpdateCommentFormDto form,
            IEnumerable<MediaFileWrapperDto>? files,
            string? mediaUploadIdentity = null) =>
            new()
            {
                Id = form.Id,
                Description = form.Description,
                GifUrl = form.GifUrl,
                VisibilityLevelId = form.VisibilityLevelId,
                Categories = form.Categories ?? new(),
                CategoriesToRemove = form.CategoriesToRemove ?? new(),
                Mentions = form.Mentions ?? new(),
                MediaItems = new CommentMediaUpdateDto
                {
                    ToAdd = ToIncomingFiles(files),
                    ToUpdate = form.MediaItems?.ToUpdate ?? new(),
                    ToRemove = form.MediaItems?.ToRemove ?? Array.Empty<int>(),
                },
                MediaUploadIdentity = mediaUploadIdentity,
            };

        private static List<IncomingMediaFile> ToIncomingFiles(
            IEnumerable<MediaFileWrapperDto>? files) =>
            (files ?? Array.Empty<MediaFileWrapperDto>())
                .Select(ToIncomingFile)
                .OfType<IncomingMediaFile>()
                .ToList();

        private static IncomingMediaFile? ToIncomingFile(MediaFileWrapperDto? wrapper)
        {
            if (wrapper is null || wrapper.File is not { } file)
                return null;

            return new IncomingMediaFile(
                file.FileName,
                file.ContentType,
                file.Length,
                wrapper.MediaType,
                file.OpenReadStream);
        }
    }

    public static class CommentMediaFormValidator
    {
        public static IReadOnlyList<string> Validate(
            IEnumerable<MediaFileWrapperDto>? files)
        {
            var errors = new List<string>();
            foreach (var wrapper in files ?? Array.Empty<MediaFileWrapperDto>())
            {
                if (wrapper?.File is null)
                {
                    errors.Add("A media entry is missing its file.");
                    continue;
                }

                try
                {
                    CommentMediaRules.EnsureValid(
                        wrapper.File.FileName,
                        wrapper.File.ContentType,
                        wrapper.File.Length,
                        wrapper.MediaType);
                }
                catch (CommentException exception)
                {
                    errors.Add(exception.Message);
                }
            }

            return errors;
        }
    }
}
