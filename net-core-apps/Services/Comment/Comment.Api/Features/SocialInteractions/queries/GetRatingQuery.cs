using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Comment.Infra.Persistence;
using Rating = Comment.Domain.AggregatesModel.CommentSocialRating;

namespace Comment.API.Features.SocialInteractions
{
    public record GetRatingQuery(CommentContext _context) : Query<int, Rating>
    {
        public override Task<Rating> Handle(int id) =>
            _context.CommentsRating.FirstOrDefaultAsync(e => e.CommentId == id);
    }
}
