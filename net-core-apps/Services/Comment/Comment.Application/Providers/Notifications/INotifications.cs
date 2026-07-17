using StarterEntities;

namespace Comment.Application.Providers.Notifications
{
    public interface INotifications
    {
        void PostShared(int userId, int sharerId, string genericCode, PlatformContexts platformContext);
        void PostCommented(int userId, int commentatorId, string genericCode, PlatformContexts platformContext);
        void LikeReceived(int userId, int likerId, string genericCode, PlatformContexts platformContext);
        void MentionReceived(int userId, int sourceUserId, string genericCode, PlatformContexts platformContext);
    }
}
