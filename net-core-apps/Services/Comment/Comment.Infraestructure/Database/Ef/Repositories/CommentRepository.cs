using Comment.Application.Abstractions.Persistence;
using Comment.Domain.AggregatesModel;
using EfCoreFilters;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Repositories
{
    public sealed class CommentRepository(CommentContext context) : ICommentRepository
    {
        private readonly CommentContext _context = context;

        public Task<CommentPost> GetByIdAsync(int id) =>
            _context.Comments.FirstOrDefaultAsync(comment => comment.Id == id);

        public Task<CommentPost> AddAsync(CommentPost comment) =>
            _context.Transaction(async commit =>
            {
                _context.Comments.Add(comment);
                await _context.SaveChangesAsync();

                _context.CommentsRating.Add(new CommentSocialRating(comment.Id, comment.Social));
                await _context.SaveEntitiesAsync();
                await commit();
                return comment;
            });

        public async Task<CommentPost> RemoveOwnedAsync(int id, int ownerUserId)
        {
            var comment = await _context.Comments.FirstOrDefaultAsync(existing =>
                existing.Id == id && existing.User.Id == ownerUserId);
            if (comment is null) return null;

            return await _context.Transaction(async commit =>
            {
                var rating = await _context.CommentsRating.FirstOrDefaultAsync(existing =>
                    existing.CommentId == comment.Id);

                _context.Comments.Remove(comment);
                if (rating is not null)
                    _context.CommentsRating.Remove(rating);

                await _context.SaveChangesAsync();
                await commit();
                return comment;
            });
        }

        public Task<CommentPost> SaveUpdatedAsync(CommentPost comment) =>
            _context.Transaction(async commit =>
            {
                await _context.SaveEntitiesAsync();
                await commit();
                return comment;
            });
    }
}
