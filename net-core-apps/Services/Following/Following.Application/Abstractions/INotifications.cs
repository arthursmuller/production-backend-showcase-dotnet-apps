namespace Following.Application.Abstractions
{
    public interface INotifications
    {
        void NewFollower(int userId, int followerId);
        void FriendRequestAccepted(int userId, int approverId);
    }
}
