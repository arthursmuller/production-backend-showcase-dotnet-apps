using MessageBrokerRabbitMq;
using MessageBrokerCore.Constants;
using NotificationProvider.Abstractions;
using NotificationProvider.Dtos;
using NotificationProvider.Providers.MessageBroker.Messages;

namespace NotificationProvider.Providers.MessageBroker.RabbitMq;

public class NotificationRabbitMqProvider : MessageBrokerRabbitMQPublisher, IBroker
{
    public NotificationRabbitMqProvider(
        IServiceProvider services,
        int retryCount) : base(services, retryCount)
    { }

    public Guid AddNotification(
        string? businessId = null,
        string? userId = null,
        string? sourceUserId = null,
        string? replacementUserId = null,
        string? sourceEntityType = null,
        string? sourceEntityId = null,
        string? title = null,
        string? message = null,
        string? description = null,
        string? templateFileName = null,
        string? template = null,
        IDictionary<string, string>? replacements = null,
        List<int>? channelIds = null,
        List<RecipientDto>? recipients = null)
        => Publish(
            new AddNotificationMessage(businessId, userId, sourceUserId, replacementUserId, sourceEntityType, sourceEntityId, title, message, description, templateFileName, template, replacements, channelIds, recipients), 
            Topology.Notification.Add.RoutingKey,
            Topology.Notification.EventsExchange
        );

}

