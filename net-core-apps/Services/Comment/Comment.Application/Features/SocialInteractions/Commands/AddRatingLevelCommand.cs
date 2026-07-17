using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Application.Extensions;
using Comment.Domain.AggregatesModel;
using StarterEntities;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record AddRatingLevelCommand(
        ICommentSocialRepository _social,
        IIdentity _auth) : Command<AddRatingLevelDto, CommentSocialAnalytics>
    {
        public override async Task<CommentSocialAnalytics> Handle(AddRatingLevelDto dto)
        {
            var rating = await _social.ExecuteWithConcurrencyRetryAsync(async () =>
            {
                var current = await _social.GetForRatingsAsync(dto.Id, _auth.User.Id);
                current.AddRating(new(
                    current.CommentId,
                    dto.RatingLevelId,
                    _auth.User.ToDomainUser(),
                    dto.Comment));
                await _social.SaveChangesAsync();
                return current;
            });

            return rating.Analytics;
        }
    }
}
