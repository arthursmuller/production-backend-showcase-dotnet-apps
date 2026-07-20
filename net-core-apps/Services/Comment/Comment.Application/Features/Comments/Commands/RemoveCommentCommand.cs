using Comment.Domain.Repositories;
using System.Threading;
using System.Threading.Tasks;
using Comment.Application.Features.Privacy;

namespace Comment.Application.Features.Comments.Commands
{
    public record RemoveCommentCommand(
        ICommentRepository _comments,
        IIdentityService _auth,
        ICommentUserErasureAuthority _erasure) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id, CancellationToken cancellationToken)
        {
            var userId = _auth.User.Id;
            await using var mutation = await _erasure.RequireActiveUsersAsync([userId], cancellationToken);
            var existing = await _comments.GetByIdAsync(id, cancellationToken);
            if (existing is null)
            {
                await mutation.CommitAsync(cancellationToken);
                return false;
            }
            existing.PrepareForRemoval(userId);
            await _comments.RemoveAsync(existing, cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            return true;
        }
    }
}
