using StarterEntities;

namespace Comment.Domain.AggregatesModel
{
    public class CommentSocialAnalytics : CommomSocialRatingAnalytics
    {
        public CommentSocialAnalytics() { }
    }
    public class CommentSocialRating : FullSocialRating<CommentSocialAnalytics, CommentLike, CommentDislike, CommentRatingLevel, CommentReport, CommentShare, CommentPostComment>
    {
        public int CommentId;
        public CommentSocialRating(int id, CommentSocialAnalytics analytics) : base(analytics) => CommentId = id;
        private CommentSocialRating() { }
    }

    public class CommentLike : CommomLike
    {
        public CommentLike() { }
        public CommentLike(User user) : base(user)
        {
        }
        public CommentLike(User user, int postId) : base(user, postId)
        {
        }
    }
    public class CommentDislike : CommomDisLike
    {
        public CommentDislike() { }
        public CommentDislike(User user) : base(user)
        {
        }
        public CommentDislike(User user, int postId) : base(user, postId)
        {
        }
    }
    public class CommentRatingLevel : CommomRatingLevel
    {
        public CommentRatingLevel() { }
        public CommentRatingLevel(int postId, int levelId, User user, string comment) : base(postId, levelId, user, comment)
        {
        }
    }

    public class CommentReport : CommomReport
    {
        public CommentReport() { }

        public CommentReport(User user, string reason, int postId) : base(user, reason, postId)
        {
        }
    }
    public class CommentPostComment : CommomComment
    {
        public CommentPostComment() { }

        public CommentPostComment(int userId, int postId) : base(userId, postId)
        {
        }
    }
    public class CommentShare : CommomShare
    {
        public CommentShare() { }
        public CommentShare(int userId, int postId) : base(userId, postId)
        {
        }
    }
    public class CommentBookmark : CommomBookmark
    {
        public CommentBookmark() { }
        public CommentBookmark(int userId, int postId, long groupId, string groupName)
            : base(userId, postId, groupId, groupName) { }
    }
}
