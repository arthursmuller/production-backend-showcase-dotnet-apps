using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Application.Extensions;
using Comment.Application.Providers.Notifications;
using StarterEntities;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record LikeCommand(
        ICommentSocialRepository _social,
        IIdentity _auth,
        IUserAnalytics _user,
        INotifications _notifications) : Command<LikeDto, bool>
    {
        public override async Task<bool> Handle(LikeDto dto)
        {
            var liked = false;
            var rating = await _social.ExecuteWithConcurrencyRetryAsync(async () =>
            {
                var current = await _social.GetForLikesAsync(dto.Id, _auth.User.Id);
                liked = current.Like(_auth.User.ToDomainUser(), current.CommentId, dto.Liked);
                await _social.SaveChangesAsync();
                return current;
            });

            if (rating is null) return false;

            var post = await _social.GetPostAsync(dto.Id, asNoTracking: true);
            if (post is null) return false;

            if (liked)
                _notifications.LikeReceived(
                    post.User.Id,
                    _auth.User.Id,
                    $"{post.Id}",
                    PlatformContexts.Comment);

            _user.UpdateLikes(post.User.Id, liked);

            return true;
        }
    }
}
