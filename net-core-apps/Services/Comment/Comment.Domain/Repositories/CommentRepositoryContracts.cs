using Comment.Domain.AggregatesModel;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Domain.Repositories
{
    public sealed class CommentQueryCriteria
    {
        public int? PageSize { get; init; }
        public DateTime?[]? DateRange { get; init; }
        public int[]? Ids { get; init; }
        public int? UserId { get; init; }
        public CommentTarget[]? Targets { get; init; }
    }

    public sealed record CommentTarget(string TargetId, string TargetType);

    /// <summary>
    /// Stable keyset position for reverse-chronological comment traversal. The
    /// unique Id tie-breaker prevents duplicates or omissions when timestamps match.
    /// </summary>
    public sealed record CommentCursorPosition(DateTime CreatedDateUtc, int Id);

    public static class CommentReadLimits
    {
        public const int MaximumCursorPageSize = 50;
    }

    /// <summary>
    /// A serialization-oriented read model for the bounded cursor path. It is
    /// deliberately made from values instead of a <see cref="CommentPost"/> so a
    /// list request never materializes or tracks an aggregate graph.
    /// </summary>
    public sealed record CommentCursorReadModel(
        int Id,
        DateTime CreatedDateUtc,
        string? Description,
        string? GifUrl,
        string TargetId,
        string TargetType,
        int AuthorUserId,
        string AuthorDisplayName,
        int VisibilityLevelId,
        IReadOnlyList<CommentCategoryCursorReadModel> Categories,
        IReadOnlyList<CommentMediaCursorReadModel> MediaItems,
        IReadOnlyList<CommentMentionCursorReadModel> Mentions);

    public sealed record CommentCategoryCursorReadModel(string Name);

    public sealed record CommentMediaCursorReadModel(
        int Id,
        int Order,
        string Url,
        string FileName,
        MediaType MediaType,
        string? ThumbnailUrl,
        long? DurationSeconds,
        string MimeType,
        DateTime CreatedDateUtc);

    public sealed record CommentMentionCursorReadModel(
        int UserId,
        string UserName);

    public sealed record CommentCursorSlice(
        IReadOnlyList<CommentCursorReadModel> Items,
        bool HasMore);

    public sealed record CommentInteractionStatus(
        int CommentId,
        bool Liked,
        bool Disliked,
        bool Rated);

    public interface ICommentRepository
    {
        Task<CommentPost> AddAsync(CommentPost comment, CancellationToken cancellationToken);
        Task<CommentPost?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task RemoveAsync(CommentPost comment, CancellationToken cancellationToken);
        Task<CommentPost> SaveUpdatedAsync(CommentPost comment, CancellationToken cancellationToken);
    }

    public interface ICommentReadRepository
    {
        Task<CommentPost?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<CommentCursorSlice> ListCursorAsync(
            CommentQueryCriteria criteria,
            CommentCursorPosition? after,
            int pageSize,
            CancellationToken cancellationToken);
        Task<CommentCursorSlice> ListOwnedCursorAsync(
            CommentQueryCriteria criteria,
            CommentCursorPosition? after,
            int pageSize,
            int ownerUserId,
            CancellationToken cancellationToken);
        Task<IReadOnlyList<CommentInteractionStatus>> GetInteractionStatusesAsync(
            int[] commentIds,
            int userId,
            CancellationToken cancellationToken);
    }
}
