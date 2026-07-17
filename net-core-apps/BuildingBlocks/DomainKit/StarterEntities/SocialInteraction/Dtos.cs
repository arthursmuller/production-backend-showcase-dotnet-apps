namespace StarterEntities
{
    public record LikeDto(int Id, bool Liked);
    public record DislikeDto(int Id, bool Disliked);
    public record AddRatingLevelDto(int Id, string Comment, int RatingLevelId);
    public record CommomReportDto(int Id, string Reason);
    public record ReportAddCommentDto(int Id, int ReportId, string Reply);
    public record UpdateCommentCountDto(string Id, bool Inrement, int UserId);
    public class AddViewDto
    {
        public string OfferId { get; set; }
        public int Id { get; set; }
    }
    public class CheckSocialInteractionsDto
    {
        public int PostId { get; set; }
        public bool Liked { get; set; }
        public bool Disliked { get; set; }
        public bool Rated { get; set; }
        public bool Reported { get; set; }
        public bool Commented { get; set; }
        public bool Shared { get; set; }
        public bool Bookmarked { get; set; }

        public CheckSocialInteractionsDto() { }
        public CheckSocialInteractionsDto(int postId, bool liked, bool disliked, bool rated, bool reported, bool commented, bool shared, bool bookmarked = false)
        {
            PostId = postId;
            Liked = liked;
            Disliked = disliked;
            Rated = rated;
            Reported = reported;
            Commented = commented;
            Shared = shared;
            Bookmarked = bookmarked;
        }
    }
}
