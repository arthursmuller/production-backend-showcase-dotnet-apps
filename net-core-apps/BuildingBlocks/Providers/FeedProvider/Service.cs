using FeedProvider.Abstractions;

namespace FeedProvider
{
    public class FeedService : IFeedService
    {
        public IBroker Broker => _broker;

        private readonly IBroker _broker;
        public FeedService(IBroker broker) => (_broker) = (broker);
    }
}
