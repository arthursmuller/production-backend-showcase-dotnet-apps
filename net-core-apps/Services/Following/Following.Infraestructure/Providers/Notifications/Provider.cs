using Microsoft.Extensions.Logging;
using NotificationProvider.Abstractions;
using NotificationProvider.Dtos;
using System.Collections.Generic;
using Following.Application.Abstractions;

namespace Following.Infra.Providers.Notifications
{
    public class Notifications : INotifications
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<Notifications> _logger;
        public Notifications(
            INotificationService notificationService,
            ILogger<Notifications> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }
        public void NewFollower(int userId, int followerId)
        {
            _notificationService.Broker.AddNotification(
               userId: $"{userId}",
               title: "Novo seguidor",
               message: $"{NotificationMessageReplacements.UserName.Name} começou a seguir você",
               channelIds: new List<int>
               {
                    NotificationChannel.Platform.Id
               },
                replacementUserId: $"{followerId}"
           );
        }

        public void FriendRequestAccepted(int userId, int approverId)
        {
            _notificationService.Broker.AddNotification(
                userId: $"{userId}",
                title: "Pedido de amizade aceito",
                message: $"{NotificationMessageReplacements.UserName.Name} aceitou seu pedido de amizado",
                channelIds: new List<int>
                {
                    NotificationChannel.Platform.Id
                },
                replacementUserId: $"{approverId}"
           );
        }

    }
}
