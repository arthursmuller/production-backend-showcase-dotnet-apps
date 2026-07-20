using Comment.Application.Providers.Storage;
using Comment.Domain.Dtos;
using Comment.Domain.Repositories;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Comment.Application.Dtos
{
    public sealed class CommentsCriteria
    {
        public int? PageSize { get; set; }
        public DateTime?[]? DateRange { get; set; }
        public int[]? Ids { get; set; }
        public int? UserId { get; set; }
        public CommentTargetFilter[]? Targets { get; set; }

        public CommentQueryCriteria ToRepositoryCriteria() => new()
        {
            PageSize = PageSize,
            DateRange = DateRange,
            Ids = Ids,
            UserId = UserId,
            Targets = Targets?
                .Select(item => new CommentTarget(item.TargetId, item.TargetType))
                .ToArray(),
        };
    }

    public sealed class CommentTargetFilter
    {
        public string TargetId { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
    }

    public sealed class AddCommentPostDto : AddCommentDomainDto
    {
        public List<IncomingMediaFile> MediaItems { get; set; } = new();
        public string? MediaUploadIdentity { get; set; }
    }

    public sealed class UpdateCommentDto : UpdateCommentDomainDto<CommentMediaUpdateDto>
    {
        public int Id { get; set; }
        public string? MediaUploadIdentity { get; set; }

        public UpdateCommentDomainDto ToDomain() => new()
        {
            Description = Description,
            GifUrl = GifUrl,
            VisibilityLevelId = VisibilityLevelId,
            Categories = Categories ?? new(),
            CategoriesToRemove = CategoriesToRemove ?? new(),
            Mentions = Mentions ?? new(),
            MediaItems = new CommentMediaUpdateDomainDto
            {
                ToUpdate = MediaItems?.ToUpdate ?? new(),
                ToRemove = MediaItems?.ToRemove ?? Array.Empty<int>(),
            },
        };
    }

    public sealed class CommentMediaUpdateDto : CommentMediaUpdateDomainDto
    {
        public List<IncomingMediaFile> ToAdd { get; set; } = new();
    }
}
