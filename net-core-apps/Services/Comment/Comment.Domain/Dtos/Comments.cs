using Comment.Domain.AggregatesModel;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;

namespace Comment.Domain.Dtos
{
    /// <summary>
    /// Domain data required to create a comment. Transport concerns such as
    /// uploaded files and streams belong to the outer request DTO.
    /// </summary>
    public class AddCommentDomainDto
    {
        public string? Description { get; set; }
        public string? TargetId { get; set; }
        public string? TargetType { get; set; }
        public string? GifUrl { get; set; }
        public int? VisibilityLevelId { get; set; }
        public List<CategoryName> Categories { get; set; } = new();
        public List<UserMention> Mentions { get; set; } = new();
    }

    public abstract class UpdateCommentDomainDto<TMedia>
        where TMedia : CommentMediaUpdateDomainDto, new()
    {
        public string? Description { get; set; }
        public string? GifUrl { get; set; }
        public int? VisibilityLevelId { get; set; }
        public List<CategoryName> Categories { get; set; } = new();
        public List<CategoryName> CategoriesToRemove { get; set; } = new();
        public TMedia MediaItems { get; set; } = new();
        public List<UserMention> Mentions { get; set; } = new();
    }

    /// <summary>
    /// The update shape consumed by the aggregate. The entity id and incoming
    /// file streams are deliberately supplied by the outer layers.
    /// </summary>
    public sealed class UpdateCommentDomainDto
        : UpdateCommentDomainDto<CommentMediaUpdateDomainDto>
    { }

    public class CommentMediaUpdateDomainDto
    {
        public List<UpdateMediaItemDomainDto> ToUpdate { get; set; } = new();
        public int[] ToRemove { get; set; } = Array.Empty<int>();
    }
}
