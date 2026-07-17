using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using StarterEntities;
using System.Linq;
using System.Threading.Tasks;

namespace Comment.Application.Features.Comments.Commands
{
    public record RemoveCommentCommand(
        ICommentRepository _comments,
        IIdentity _auth,
        IFeed _feed,
        IPosts _posts,
        ICategories _categories) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id)
        {
            var userId = _auth.User.Id;
            var existing = await _comments.RemoveOwnedAsync(id, userId);
            if (existing is null) return false;

            _posts.CommentCreated(
                existing.IdOfRelatedEntity,
                false,
                userId,
                existing.TypeOfRelatedEntity.ToLower());

            if (existing.PublishedOnFeed)
                _feed.Remove(PlatformContexts.Comment.Name, $"{existing.Id}");

            _categories.UpdateMentions(
                null,
                existing.Categories?.Select(category => category.Name).ToArray());

            return true;
        }
    }
}
