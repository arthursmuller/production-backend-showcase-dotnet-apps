namespace NotificationProvider.Abstractions
{
    public interface INotificationService
    {
        public IBroker Broker { get; }
    }
}
