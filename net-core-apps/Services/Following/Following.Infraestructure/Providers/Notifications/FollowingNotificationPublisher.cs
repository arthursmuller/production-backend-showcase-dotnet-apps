using Following.Application.Providers.Notifications;
using Following.Infra.Persistence;
using MessageBrokerContracts.Notification;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Infra.Providers.Notifications
{
    public sealed class FollowingNotificationPublisher(
        IIntegrationMessagePublisher publisher,
        FollowingContext context,
        TimeProvider timeProvider) : INotifications
    {
        private const int PlatformChannelId = 1;
        private const string UserNamePlaceholder = "{username}";

        public async Task AddPlatformNotificationAsync(
            int recipientUserId,
            string title,
            string message,
            int sourceUserId,
            CancellationToken cancellationToken)
        {
            var eventId = Guid.NewGuid();
            await context.PublishToBusOutboxAsync(
                publisher,
                new NotificationRequested
                {
                    EventId = eventId,
                    OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                    RecipientUserId = recipientUserId,
                    SourceUserId = sourceUserId,
                    ReplacementUserId = sourceUserId,
                    SourceEntityType = "user",
                    SourceEntityId = sourceUserId.ToString(CultureInfo.InvariantCulture),
                    Title = title,
                    Message = message.Replace(
                        "{UserName}",
                        UserNamePlaceholder,
                        StringComparison.Ordinal),
                    ChannelIds = [PlatformChannelId],
                },
                eventId,
                cancellationToken);
        }
    }
}
