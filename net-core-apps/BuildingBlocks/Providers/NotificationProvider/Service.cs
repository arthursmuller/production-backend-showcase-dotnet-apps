using NotificationProvider.Abstractions;

namespace NotificationProvider;

public class NotificationService(IBroker broker) : INotificationService
{
    public IBroker Broker => broker;
}

