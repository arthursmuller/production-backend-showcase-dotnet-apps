using Comment.Domain.AggregatesModel;
using System;
using System.Threading.Tasks;

namespace Comment.Application.Abstractions.Persistence
{
    public interface ICommentSocialRepository
    {
        Task<T> ExecuteWithConcurrencyRetryAsync<T>(Func<Task<T>> action);

        Task<CommentPost> GetPostAsync(int id, bool asNoTracking = false);
        Task<CommentSocialRating> GetForRatingsAsync(int commentId, int userId);
        Task<CommentSocialRating> GetForDislikesAsync(int commentId, int userId);
        Task<CommentSocialRating> GetForLikesAsync(int commentId, int userId);
        Task<CommentSocialRating> GetForReportsAsync(int commentId, int userId);
        Task<CommentSocialRating> GetForCommentsAsync(int commentId, int userId);
        Task<CommentSocialRating> GetForSharesAsync(int commentId, int userId);

        Task<CommentBookmark> FindBookmarkAsync(int ownerUserId, int postId, long groupId);
        void AddBookmark(CommentBookmark bookmark);
        void RemoveBookmark(CommentBookmark bookmark);
        Task SaveChangesAsync();
    }
}
