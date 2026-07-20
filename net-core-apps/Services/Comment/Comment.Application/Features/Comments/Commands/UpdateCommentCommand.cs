using Comment.Domain.Repositories;
using Comment.Application.Dtos;
using Comment.Application.Providers.Storage;
using Comment.Application.Features.Media.Commands;
using Comment.Domain.AggregatesModel;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Comment.Application.Features.Privacy;

namespace Comment.Application.Features.Comments.Commands
{
    public record UpdateCommentCommand(
        ICommentRepository _comments,
        IIdentityService _auth,
        CommentMediaIngestionService _media,
        ICommentUserErasureAuthority _erasure) : Command<UpdateCommentDto, CommentPost?>
    {
        public override async Task<CommentPost?> Handle(
            UpdateCommentDto dto,
            CancellationToken cancellationToken)
        {
            var existing = await _comments.GetByIdAsync(dto.Id, cancellationToken);
            if (existing is null)
                return null;

            var userId = _auth.User.Id;
            await using var mutation = await _erasure.RequireActiveUsersAsync(
                new[] { userId }.Concat((dto.Mentions ?? []).Select(value => value.UserId)),
                cancellationToken);
            existing.EnsureOwnedBy(userId);
            var additions = dto.MediaItems?.ToAdd;
            if (additions is null || additions.Count == 0)
            {
                existing.Update(userId, dto.ToDomain());
                var updated = await _comments.SaveUpdatedAsync(existing, cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                return updated;
            }

            var media = await _media.IngestAsync(
                additions,
                userId,
                dto.MediaUploadIdentity,
                "update",
                dto.Id,
                cancellationToken);
            if (media.Ingestion.BoundCommentId.HasValue)
            {
                var replay = await _comments.GetByIdAsync(dto.Id, cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                return replay;
            }

            existing.Update(userId, dto.ToDomain());
            existing.AttachMedia(media.Media);
            var result = await _media.BindUpdatedCommentAsync(
                media.Ingestion,
                existing,
                cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            return result;
        }
    }
}
