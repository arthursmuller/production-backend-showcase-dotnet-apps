namespace Comment.Application.Abstractions
{
    public interface IUserAnalytics
    {
        void UpdateLikes(int userId, bool increment);
        void UpdateViews(int userId, bool increment);
    }
}
