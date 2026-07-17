using RichEnumeration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarterEntities
{
    public abstract class CommomSocialRatingAnalytics : BaseEntity
    {
        public int TotalViews { get; set; }
        public int TotalRatings { get; set; }
        public int TotalReports { get; set; }
        public int TotalLikes { get; set; }
        public int TotalDislikes { get; set; }
        public int TotalShares { get; set; }
        public int TotalComments { get; set; }
        public double RatingsTrueCount { get; set; }
        public double AverageRating
        {
            get => TotalRatings == 0 ? 0 : RatingsTrueCount / TotalRatings;
            private set => _averageRating = value;
        }
        private double _averageRating;

    }
    public abstract class CommomRating<TLike, TDislike, TRating, TReport, TShare, TComment> : BaseEntity
        where TLike : CommomLike
        where TDislike : CommomDisLike
        where TRating : CommomRatingLevel
        where TReport : CommomReport
        where TShare : CommomShare
        where TComment : CommomComment
    {
        public List<TRating> Ratings { get; set; } = new();
        public List<TReport> Reports { get; set; } = new();
        public List<TLike> Likes { get; set; } = new();
        public List<TDislike> Dislikes { get; set; } = new();
        public List<TComment> Comments { get; set; } = new();
        public List<TShare> Shares { get; set; } = new();

        protected CommomRating() { }
        public virtual bool Like(User user, int postId, bool liked)
        {
            var existingLike = Likes.FirstOrDefault(e => e.User.Id == user.Id);

            if (existingLike is not null)
               return !Likes.Remove(existingLike);
            else
                Likes.Add(Activator.CreateInstance(typeof(TLike), user, postId) as TLike);

            return true;
        }

        public virtual bool Dislike(User user, int postId, bool disliked)
        {
            var existingdislike = Dislikes.FirstOrDefault(e => e.User.Id == user.Id);

            if (existingdislike is not null)
                return !Dislikes.Remove(existingdislike);
            else
                Dislikes.Add(Activator.CreateInstance(typeof(TDislike), user, postId) as TDislike);

            return true;
        }

        public virtual void AddComment(TComment comment, bool increment)
        {
            if (increment)
                Comments.Add(comment);
            else
            {
                var existing = Comments.FirstOrDefault(e => e.UserId == comment.UserId);
                if (existing is not null)
                    Comments.Remove(existing);
            }
        }

        public virtual void AddShare(TShare share, bool increment)
        {
            if (increment)
                Shares.Add(share);
            else
            {
                var existing = Shares.FirstOrDefault(e => e.UserId == share.UserId);
                if (existing is not null)
                    Shares.Remove(existing);
            }
        }
    }
    public abstract class CommomLike : BaseEntity
    {
        public User User { get; set; }
        public int PostId { get; set; }
        
        protected CommomLike() { }
        protected CommomLike(User user)
        {
            User = user;
        }
        protected CommomLike(User user, int postId)
        {
            User = user;
            PostId = postId;
        }
    }
    public abstract class CommomDisLike : BaseEntity
    {
        public User User { get; set; }
        public int PostId { get; set; }

        protected CommomDisLike() { }
        protected CommomDisLike(User user)
        {
            User = user;
        }
        protected CommomDisLike(User user, int postId)
        {
            User = user;
            PostId = postId;
        }
    }
    public abstract class CommomComment : BaseEntity
    {
        public int UserId { get; set; }
        public int PostId { get; set; }

        protected CommomComment() { }
        protected CommomComment(int userId, int postId)
        {
            UserId = userId;
            PostId = postId;
        }
    }
    public abstract class CommomShare : BaseEntity
    {
        public int UserId { get; set; }
        public int PostId { get; set; }

        protected CommomShare() { }
        protected CommomShare(int userId, int postId)
        {
            UserId = userId;
            PostId = postId;
        }
    }
    public abstract class CommomBookmark : BaseEntity
    {
        public int UserId { get; set; }
        public int PostId { get; set; }
        public long GroupId { get; set; }
        public string GroupName { get; set; }

        protected CommomBookmark() { }
        protected CommomBookmark(int userId, int postId, long groupId, string groupName)
        {
            UserId = userId;
            PostId = postId;
            GroupId = groupId;
            GroupName = groupName;
        }
    }
    public abstract class CommomRatingLevel : BaseEntity
    {
        public int PostId { get; set; }
        public RatingLevel Level { get; set; }
        private int _levelId;
        public User User { get; set; }
        public string Comment { get; set; }

        protected CommomRatingLevel() { }
        protected CommomRatingLevel(int postId, int levelId, User user, string comment)
        {
            PostId = postId;
            _levelId = levelId;
            User = user;
            Comment = comment;
        }

        public RatingLevel GetLevel() => RatingLevel.From(_levelId);
    }
    public abstract class CommomReport : BaseEntity
    {
        public string Reason { get; set; }
        public string Reply { get; set; }
        public bool? Approved { get; set; }
        public int PostId { get; set; }
        public User User { get; set; }

        protected CommomReport() { }
        protected CommomReport(User user, string reason, int postId)
        {
            User = user;
            Reason = reason;
            PostId = postId;
            Approved = null;
        }

        public void AddReply(string reply) => Reply = reply;
        public void Approve() => Approved = true;
    }
    public abstract class FullSocialRating<TAnalytics, TLike, TDislike, TRating, TReport, TShare, TComment> : CommomRating<TLike, TDislike, TRating, TReport, TShare, TComment> 
        where TAnalytics : CommomSocialRatingAnalytics, new()
        where TLike : CommomLike
        where TDislike : CommomDisLike
        where TRating : CommomRatingLevel
        where TReport : CommomReport
        where TShare : CommomShare
        where TComment : CommomComment
    {
        public TAnalytics Analytics { get; set; }

        public FullSocialRating() { }
        public FullSocialRating(TAnalytics analytics) => Analytics = analytics;

        public override bool Like(User user, int postId, bool isLiked)
        {
            var liked = base.Like(user, postId, isLiked);
            Analytics.TotalLikes += liked ? 1 : -1;
            UpdateDate = DateTime.UtcNow;
            return liked;
        }
        public override bool Dislike(User user, int postId, bool isDisliked)
        {
            var disliked = base.Dislike(user, postId, isDisliked);
            Analytics.TotalDislikes += disliked ? 1 : -1;

            return disliked;
        }
        public void AddRating(TRating rating)
        {
            var existingRating = Ratings.FirstOrDefault(e => e.User.Id == rating.User.Id);
            if (existingRating is not null)
            {
                Ratings.Remove(existingRating);
                Analytics.TotalRatings--;
                Analytics.RatingsTrueCount -= existingRating.GetLevel().Id;
            }

            Ratings.Add(rating);
            Analytics.TotalRatings++;
            Analytics.RatingsTrueCount += rating.GetLevel().Id;
        }
        public void AddReport(TReport report)
        {
            var existingReport = Reports.FirstOrDefault(e => e.User.Id == report.User.Id);
            if (existingReport is not null)
            {
                Reports.Remove(existingReport);
                Analytics.TotalReports--;
            }
            Reports.Add(report);
            Analytics.TotalReports++;
        }

        public override void AddComment(TComment comment, bool increment)
        {
            base.AddComment(comment, increment);
            Analytics.TotalComments += increment ? 1 : -1;
        }

        public override void AddShare(TShare share, bool increment)
        {
            base.AddShare(share, increment);
            Analytics.TotalShares += increment ? 1 : -1;
        }
    }
    public class RatingLevel : Enumeration
    {
        public string Description { get; set; }

        public static RatingLevel VeryPoor = new RatingLevel(1, nameof(VeryPoor).ToLowerInvariant(), "Muito Ruim");
        public static RatingLevel Poor = new RatingLevel(2, nameof(Poor).ToLowerInvariant(), "Ruim");
        public static RatingLevel Fair = new RatingLevel(3, nameof(Fair).ToLowerInvariant(), "Normal");
        public static RatingLevel Good = new RatingLevel(4, nameof(Good).ToLowerInvariant(), "Boa");
        public static RatingLevel Excellent = new RatingLevel(5, nameof(Excellent).ToLowerInvariant(), "Muito Bom");
        public static RatingLevel ExcellentPlus = new RatingLevel(6, nameof(ExcellentPlus).ToLowerInvariant(), "Excelente");

        public RatingLevel(int id, string name, string description) : base(id, name)
        {
            Description = description;
        }
        public RatingLevel(int id, string name) : base(id, name) { }
        public static IEnumerable<RatingLevel> List() => new[] { VeryPoor, Poor, Fair, Good, Excellent, ExcellentPlus };

        public static RatingLevel From(int id)
        {
            var state = List().SingleOrDefault(s => s.Id == id);
            return state;
        }
    }
    public class VisibilityLevel : Enumeration
    {
        public string Description { get; set; }

        public static VisibilityLevel Global = new VisibilityLevel(1, nameof(Global).ToLowerInvariant(), "Todos");
        public static VisibilityLevel Friends = new VisibilityLevel(2, nameof(Friends).ToLowerInvariant(), "Amigos");
        public static VisibilityLevel FriendsOfFriends = new VisibilityLevel(3, nameof(FriendsOfFriends).ToLowerInvariant(), "Amigos de amigos");
        public static VisibilityLevel Private = new VisibilityLevel(4, nameof(Private).ToLowerInvariant(), "Eu");

        public VisibilityLevel(int id, string name, string description) : base(id, name)
        {
            Description = description;
        }

        public static IEnumerable<VisibilityLevel> List() => new[] { Global, Friends, FriendsOfFriends, Private };
    }
    public record VisibilityLevelRecord(int Id, string Name)
    {
        public static explicit operator string(VisibilityLevelRecord obj) => obj.Name;
        public static explicit operator int(VisibilityLevelRecord obj) => obj.Id;
        public bool IsVisible() =>
            Id == VisibilityLevel.Global.Id ||
            Id == VisibilityLevel.Friends.Id ||
            Id == VisibilityLevel.FriendsOfFriends.Id;
             
        public bool IsNotVisible() => Id == VisibilityLevel.Private.Id;
    };
   
}

