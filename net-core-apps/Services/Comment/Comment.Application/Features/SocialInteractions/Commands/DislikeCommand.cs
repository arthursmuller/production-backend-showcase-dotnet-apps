using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Application.Extensions;
using Comment.Domain.AggregatesModel;
using StarterEntities;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record DislikeCommand(
        ICommentSocialRepository _social,
        IIdentity _auth) : Command<DislikeDto, CommentSocialAnalytics>
    {
        public override async Task<CommentSocialAnalytics> Handle(DislikeDto dto)
        {
            var rating = await _social.ExecuteWithConcurrencyRetryAsync(async () =>
            {
                var current = await _social.GetForDislikesAsync(dto.Id, _auth.User.Id);
                current.Dislike(_auth.User.ToDomainUser(), current.CommentId, dto.Disliked);
                await _social.SaveChangesAsync();
                return current;
            });

            return rating.Analytics;
        }
    }
}
