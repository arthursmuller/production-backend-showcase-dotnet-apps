using Microsoft.Extensions.Logging;
using Comment.Application.Providers.Notifications;
using NotificationProvider.Abstractions;
using NotificationProvider.Dtos;
using StarterEntities;
using System;
using System.Collections.Generic;

namespace Comment.Infra.Providers.Notifications
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
        public void PostCommented(int userId, int commentatorId, string genericCode, PlatformContexts platformContext)
        {
            _notificationService.Broker.AddNotification(
                userId: $"{userId}",
                title: "Comentário em publicação",
                message: $"{NotificationMessageReplacements.UserName.Name} compartilhou sua publicação {platformContext.Description}",
                channelIds: new List<int>
                {
                    NotificationChannel.Platform.Id
                },
                replacementUserId: $"{commentatorId}",
                sourceUserId: $"{commentatorId}",
                sourceEntityId: genericCode,
                sourceEntityType: platformContext.Name
            );
        }
        public void LikeReceived(int userId, int likerId, string genericCode, PlatformContexts platformContext)
        {
            _notificationService.Broker.AddNotification(
                userId: $"{userId}",
                title: "Curtida em publicação",
                message: $"{NotificationMessageReplacements.UserName.Name} curtiu sua publicação - Quote/1",
                channelIds: new List<int>
                {
                    NotificationChannel.Platform.Id
                },
                replacementUserId: $"{likerId}",
                sourceUserId: $"{likerId}",
                sourceEntityId: genericCode,
                sourceEntityType: platformContext.Name
            );
        }

        public void PostShared(int userId, int sharerId, string genericCode, PlatformContexts platformContext)
        {
            _notificationService.Broker.AddNotification(
                userId: $"{userId}",
                title: "Compartilhamento em publicação",
                message: $"{NotificationMessageReplacements.UserName.Name} compartilhou sua publicação {platformContext.Description}",
                channelIds: new List<int>
                {
                    NotificationChannel.Platform.Id
                },
                replacementUserId: $"{sharerId}",
                sourceUserId: $"{sharerId}",
                sourceEntityId: genericCode,
                sourceEntityType: platformContext.Name
            );
        }

        public void MentionReceived(int userId, int sourceUserId, string genericCode, PlatformContexts platformContext)
        {
            _notificationService.Broker.AddNotification(
                userId: $"{userId}",
                title: "Você foi mencionado!",
                message: $"{NotificationMessageReplacements.UserName.Name} mencionou você em um comentário",
                channelIds: new List<int>
                {
                    NotificationChannel.Platform.Id
                },
                replacementUserId: $"{sourceUserId}",
                sourceUserId: $"{sourceUserId}",
                sourceEntityId: genericCode,
                sourceEntityType: platformContext.Name
            );
        }

    }
}
