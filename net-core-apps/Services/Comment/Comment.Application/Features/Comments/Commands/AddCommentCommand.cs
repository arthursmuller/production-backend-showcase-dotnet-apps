using Comment.Domain.Repositories;
using Comment.Application.Dtos;
using Comment.Application.Extensions;
using Comment.Application.Providers.Storage;
using Comment.Application.Features.Media.Commands;
using System.Linq;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Exceptions;
using System.Threading;
using System.Threading.Tasks;
using Comment.Application.Features.Privacy;

namespace Comment.Application.Features.Comments.Commands
{
    public record AddCommentCommand(
        ICommentRepository _comments,
        IIdentityService _auth,
        CommentMediaIngestionService _media,
        ICommentUserErasureAuthority _erasure) : Command<AddCommentPostDto, CommentPost>
    {
        public override async Task<CommentPost> Handle(
            AddCommentPostDto dto,
            CancellationToken cancellationToken)
        {
            var userId = _auth.User.Id;
            await using var mutation = await _erasure.RequireActiveUsersAsync(
                new[] { userId }.Concat((dto.Mentions ?? []).Select(value => value.UserId)),
                cancellationToken);
            if (dto.MediaItems is null || dto.MediaItems.Count == 0)
            {
                var withoutMedia = new CommentPost(
                    _auth.User.ToDomainUser(),
                    dto,
                    System.Array.Empty<NewCommentMedia>());
                var added = await _comments.AddAsync(withoutMedia, cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                return added;
            }

            var media = await _media.IngestAsync(
                dto.MediaItems,
                userId,
                dto.MediaUploadIdentity,
                "create",
                null,
                cancellationToken);
            if (media.Ingestion.BoundCommentId.HasValue)
            {
                var replay = await _comments.GetByIdAsync(
                    media.Ingestion.BoundCommentId.Value,
                    cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                return replay ?? throw new CommentException(
                    "The previously bound comment could not be recovered.",
                    409);
            }

            var comment = new CommentPost(
                _auth.User.ToDomainUser(),
                dto,
                media.Media);
            var result = await _media.BindNewCommentAsync(
                media.Ingestion,
                comment,
                cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            return result;
        }
    }
}
