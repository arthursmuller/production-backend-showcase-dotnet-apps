using Comment.Application.Abstractions.Persistence;
using Comment.Domain.AggregatesModel;
using Comment.Infra.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Repositories
{
    public sealed class CommentSocialRepository(CommentContext context) : ICommentSocialRepository
    {
        private readonly CommentContext _context = context;

        public Task<T> ExecuteWithConcurrencyRetryAsync<T>(Func<Task<T>> action) =>
            Retry.DbConcurrency.ExecuteAsync(action);

        public Task<CommentPost> GetPostAsync(int id, bool asNoTracking = false)
        {
            IQueryable<CommentPost> query = _context.Comments;
            if (asNoTracking)
                query = query.AsNoTracking();

            return query.FirstOrDefaultAsync(post => post.Id == id);
        }

        public Task<CommentSocialRating> GetForRatingsAsync(int commentId, int userId) =>
            _context.CommentsRating
                .Include(rating => rating.Ratings.Where(item => item.User.Id == userId))
                .FirstOrDefaultAsync(rating => rating.CommentId == commentId);

        public Task<CommentSocialRating> GetForDislikesAsync(int commentId, int userId) =>
            _context.CommentsRating
                .Include(rating => rating.Dislikes.Where(item => item.User.Id == userId))
                .FirstOrDefaultAsync(rating => rating.CommentId == commentId);

        public Task<CommentSocialRating> GetForLikesAsync(int commentId, int userId) =>
            _context.CommentsRating
                .Include(rating => rating.Likes.Where(item => item.User.Id == userId))
                .FirstOrDefaultAsync(rating => rating.CommentId == commentId);

        public Task<CommentSocialRating> GetForReportsAsync(int commentId, int userId) =>
            _context.CommentsRating
                .Include(rating => rating.Reports.Where(item => item.User.Id == userId))
                .FirstOrDefaultAsync(rating => rating.CommentId == commentId);

        public Task<CommentSocialRating> GetForCommentsAsync(int commentId, int userId) =>
            _context.CommentsRating
                .Include(rating => rating.Comments.Where(item => item.UserId == userId))
                .FirstOrDefaultAsync(rating => rating.CommentId == commentId);

        public Task<CommentSocialRating> GetForSharesAsync(int commentId, int userId) =>
            _context.CommentsRating
                .Include(rating => rating.Shares.Where(item => item.UserId == userId))
                .FirstOrDefaultAsync(rating => rating.CommentId == commentId);

        public Task<CommentBookmark> FindBookmarkAsync(int ownerUserId, int postId, long groupId) =>
            _context.CommentBookmarks.FirstOrDefaultAsync(bookmark =>
                bookmark.UserId == ownerUserId &&
                bookmark.PostId == postId &&
                bookmark.GroupId == groupId);

        public void AddBookmark(CommentBookmark bookmark) =>
            _context.CommentBookmarks.Add(bookmark);

        public void RemoveBookmark(CommentBookmark bookmark) =>
            _context.CommentBookmarks.Remove(bookmark);

        public async Task SaveChangesAsync() =>
            await _context.SaveChangesAsync();
    }
}
