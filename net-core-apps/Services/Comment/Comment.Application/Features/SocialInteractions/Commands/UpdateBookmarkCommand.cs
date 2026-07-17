using Comment.Application.Abstractions.Persistence;
using Comment.Application.MessageBrokers.Messages;
using Comment.Domain.AggregatesModel;
using System;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record UpdateBookmarkCommand(
        ICommentSocialRepository _social) : Command<BookmarkChangedDto, bool>
    {
        public override async Task<bool> Handle(BookmarkChangedDto dto)
        {
            if (!Int32.TryParse(dto.IdOfRelatedEntity, out var id)) return false;

            var post = await _social.GetPostAsync(id, asNoTracking: true);
            if (post is null) return false;

            await _social.ExecuteWithConcurrencyRetryAsync(async () =>
            {
                var existing = await _social.FindBookmarkAsync(
                    dto.OwnerUserId,
                    post.Id,
                    dto.GroupId);

                if (dto.Created && existing is null)
                    _social.AddBookmark(new CommentBookmark(
                        dto.OwnerUserId,
                        post.Id,
                        dto.GroupId,
                        dto.GroupName));
                else if (!dto.Created && existing is not null)
                    _social.RemoveBookmark(existing);

                await _social.SaveChangesAsync();
                return true;
            });

            return true;
        }
    }
}
