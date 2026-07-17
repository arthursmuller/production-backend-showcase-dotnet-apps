using System.Linq;
using System.Threading.Tasks;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;
using StarterEntities;
using UserProvider.Abstractions;
using Comment.Domain.AggregatesModel;
using Comment.Infra.Helpers;
using Comment.Infra.Persistence;
using Rating = Comment.Domain.AggregatesModel.CommentSocialRating;
using Social = Comment.Domain.AggregatesModel.CommentSocialAnalytics;
using Post = Comment.Domain.AggregatesModel.CommentPost;
namespace Comment.API.Features.SocialInteractions
{
    public record GetRatingLevelQuery(CommentContext _context, IIdentityService _auth) : Query<int, CommomRatingLevel>
    {
        public override async Task<CommomRatingLevel> Handle(int id) => (
            await _context.Set<Rating>()
                .Include(e => e.Ratings.Where(e => e.User.Id == _auth.User.Id))
                .FirstOrDefaultAsync(e => e.CommentId == id)
        )?.Ratings?.FirstOrDefault();
    }
}
