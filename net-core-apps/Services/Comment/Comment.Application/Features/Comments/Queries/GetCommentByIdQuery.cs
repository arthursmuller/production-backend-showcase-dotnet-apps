using Comment.Domain.Repositories;
using Comment.Application.Dtos;
using Comment.Application.Features.Interactions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Comments.Queries
{
    public record GetCommentByIdQuery(
        ICommentReadRepository Comments,
        IIdentityService Identity,
        CommentInteractionReadProjector InteractionReads) : Query<int, CommentCursorItemDto?>
    {
        public override async Task<CommentCursorItemDto?> Handle(int id, CancellationToken cancellationToken)
        {
            var comment = await Comments.GetByIdAsync(id, cancellationToken);
            if (comment is null)
                return null;

            var dto = CommentCursorDtoMapper.ToDto(comment);
            await InteractionReads.ApplyToCursorDtosAsync([dto], cancellationToken);
            var userId = Identity.User?.Id ?? 0;
            if (userId > 0)
            {
                var status = (await Comments.GetInteractionStatusesAsync(
                    [dto.Id], userId, cancellationToken)).SingleOrDefault();
                if (status is not null)
                    dto.InteractionStatus = new CommentCursorInteractionStatusDto
                    {
                        CommentId = status.CommentId,
                        Liked = status.Liked,
                        Disliked = status.Disliked,
                        Rated = status.Rated,
                    };
            }
            return dto;
        }
    }
}
