using Comment.Application.Providers.Storage;
using Comment.Domain.Repositories;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Comments.Queries;

public sealed record CommentMediaRequest(int CommentId, int MediaItemId);

public sealed record CommentMediaContent(Stream Content, string MimeType);

/// <summary>
/// Resolves media through the same audience-filtered read used by the Comment
/// endpoint. No provider URL is returned; authorization is reevaluated for every
/// object request so a visibility, moderation, or relationship change revokes
/// access immediately.
/// </summary>
public sealed record GetCommentMediaQuery(
    ICommentReadRepository Comments,
    ICommentMediaObjectStore Objects)
    : Query<CommentMediaRequest, CommentMediaContent?>
{
    public override async Task<CommentMediaContent?> Handle(
        CommentMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CommentId <= 0 || request.MediaItemId <= 0)
            return null;

        var comment = await Comments.GetByIdAsync(
            request.CommentId,
            cancellationToken);
        if (comment is null)
            return null;

        var media = comment.MediaItems.SingleOrDefault(item =>
            item.Id == request.MediaItemId);
        if (media is null)
            return null;

        var content = await Objects.OpenFinalReadAsync(
            comment.AuthorUserId,
            media.Url,
            cancellationToken);
        return new CommentMediaContent(content, media.MimeType);
    }
}
