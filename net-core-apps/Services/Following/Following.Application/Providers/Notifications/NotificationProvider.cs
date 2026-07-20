using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Providers.Notifications
{
    public interface INotifications
    {
        Task AddPlatformNotificationAsync(
            int recipientUserId,
            string title,
            string message,
            int sourceUserId,
            CancellationToken cancellationToken);
    }

    public sealed class NotificationProvider(INotifications notifications)
    {
        public Task NewFollowerAsync(
            int userId,
            int followerId,
            CancellationToken cancellationToken) =>
            notifications.AddPlatformNotificationAsync(
                userId,
                "Novo seguidor",
                "{UserName} começou a seguir você",
                followerId,
                cancellationToken);

        public Task FriendRequestAcceptedAsync(
            int userId,
            int approverId,
            CancellationToken cancellationToken) =>
            notifications.AddPlatformNotificationAsync(
                userId,
                "Pedido de amizade aceito",
                "{UserName} aceitou seu pedido de amizade",
                approverId,
                cancellationToken);
    }
}
