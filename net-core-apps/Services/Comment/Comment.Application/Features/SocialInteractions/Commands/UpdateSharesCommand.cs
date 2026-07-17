using Comment.Application.Abstractions.Persistence;
using Comment.Application.Providers.Notifications;
using Comment.Domain.AggregatesModel;
using StarterEntities;
using System;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record UpdateSharesCommand(
        ICommentSocialRepository _social,
        INotifications _notifications) : Command<UpdateCommentCountDto, bool>
    {
        public override async Task<bool> Handle(UpdateCommentCountDto dto)
        {
            if (!Int32.TryParse(dto.Id, out var id)) return false;

            var post = await _social.GetPostAsync(id, asNoTracking: true);
            if (post is null) return false;

            await _social.ExecuteWithConcurrencyRetryAsync(async () =>
            {
                var rating = await _social.GetForSharesAsync(post.Id, dto.UserId);
                rating.AddShare(new CommentShare(dto.UserId, rating.CommentId), dto.Inrement);
                await _social.SaveChangesAsync();
                return rating;
            });

            if (dto.Inrement)
                _notifications.PostShared(
                    post.User.Id,
                    dto.UserId,
                    $"{post.Id}",
                    PlatformContexts.Comment);

            return true;
        }
    }
}
