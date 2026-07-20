using Comment.Application.Dtos;
using Comment.Domain.Exceptions;
using Comment.Domain.Repositories;
using Comment.Application.Features.Interactions;
using Comment.Application.Providers.Interactions;
using Comment.Application.Dtos.Interactions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Comments.Queries
{
    public sealed record CommentCursorRequest(
        CommentsCriteria? Criteria,
        string? Cursor);

    public sealed record ListCommentsCursorQuery(
        ICommentReadRepository Comments,
        IIdentityService Identity,
        CommentCursorCodec CursorCodec,
        CommentInteractionReadProjector InteractionReads)
        : Query<CommentCursorRequest, CommentCursorPage<CommentCursorItemDto>>
    {
        public override async Task<CommentCursorPage<CommentCursorItemDto>> Handle(
            CommentCursorRequest request,
            CancellationToken cancellationToken)
        {
            var criteria = request.Criteria ?? new CommentsCriteria();
            var fingerprint = CommentCursorFingerprint.Create(criteria, "public");
            var after = Decode(request.Cursor, fingerprint);
            var pageSize = criteria.PageSize ?? 20;
            var slice = await Comments.ListCursorAsync(
                criteria.ToRepositoryCriteria(),
                after,
                pageSize,
                cancellationToken);
            var items = await ConvertWithStatusesAsync(
                slice.Items,
                Identity.User?.Id ?? 0,
                cancellationToken);
            var next = CreateNext(slice, fingerprint);
            return new CommentCursorPage<CommentCursorItemDto>(items, next, slice.HasMore);
        }

        private CommentCursorPosition? Decode(string? cursor, byte[] fingerprint)
        {
            if (string.IsNullOrWhiteSpace(cursor))
                return null;
            if (!CursorCodec.TryDecode(cursor, fingerprint, out var position))
                throw new CommentException("The comment cursor is invalid or expired.");
            return position;
        }

        private string? CreateNext(CommentCursorSlice slice, byte[] fingerprint)
        {
            if (!slice.HasMore || slice.Items.Count == 0)
                return null;

            var last = slice.Items[^1];
            return CursorCodec.Encode(
                new CommentCursorPosition(last.CreatedDateUtc, last.Id),
                fingerprint);
        }

        private async Task<IReadOnlyList<CommentCursorItemDto>> ConvertWithStatusesAsync(
            IReadOnlyList<CommentCursorReadModel> comments,
            int userId,
            CancellationToken cancellationToken)
        {
            var dtos = CommentCursorDtoMapper.ToDtos(comments).ToList();
            await InteractionReads.ApplyToCursorDtosAsync(dtos, cancellationToken);
            if (userId <= 0 || dtos.Count == 0)
                return dtos;

            var statuses = await Comments.GetInteractionStatusesAsync(
                dtos.Select(comment => comment.Id).ToArray(),
                userId,
                cancellationToken);
            var byCommentId = statuses.ToDictionary(status => status.CommentId);
            foreach (var dto in dtos)
            {
                if (byCommentId.TryGetValue(dto.Id, out var status))
                    dto.InteractionStatus = new CommentCursorInteractionStatusDto
                    {
                        CommentId = status.CommentId,
                        Liked = status.Liked,
                        Disliked = status.Disliked,
                        Rated = status.Rated,
                    };
            }

            return dtos;
        }
    }

    public sealed record ListOwnerCommentsCursorQuery(
        ICommentReadRepository Comments,
        IIdentityService Identity,
        CommentCursorCodec CursorCodec,
        CommentInteractionReadProjector InteractionReads)
        : Query<CommentCursorRequest, CommentCursorPage<CommentCursorItemDto>>
    {
        public override async Task<CommentCursorPage<CommentCursorItemDto>> Handle(
            CommentCursorRequest request,
            CancellationToken cancellationToken)
        {
            var ownerUserId = Identity.User?.Id ?? 0;
            if (ownerUserId <= 0)
                throw new CommentException("An authenticated comment owner is required.", 403);

            var criteria = request.Criteria ?? new CommentsCriteria();
            var fingerprint = CommentCursorFingerprint.Create(
                criteria,
                $"owner:{ownerUserId}");
            CommentCursorPosition? after = null;
            if (!string.IsNullOrWhiteSpace(request.Cursor) &&
                !CursorCodec.TryDecode(request.Cursor, fingerprint, out after))
                throw new CommentException("The comment cursor is invalid or expired.");

            var pageSize = criteria.PageSize ?? 20;
            var slice = await Comments.ListOwnedCursorAsync(
                criteria.ToRepositoryCriteria(),
                after,
                pageSize,
                ownerUserId,
                cancellationToken);
            var items = CommentCursorDtoMapper.ToDtos(slice.Items).ToList();
            await InteractionReads.ApplyToCursorDtosAsync(items, cancellationToken);
            var next = slice.HasMore && slice.Items.Count > 0
                ? CursorCodec.Encode(
                    new CommentCursorPosition(
                        slice.Items[^1].CreatedDateUtc,
                        slice.Items[^1].Id),
                    fingerprint)
                : null;

            return new CommentCursorPage<CommentCursorItemDto>(items, next, slice.HasMore);
        }
    }
}
