using CategoriesProvider.Abstractions;

namespace CategoriesProvider
{
    public class CategoriesService : ICategoriesService
    {
        public ICategoriesApi Api => _api;
        private readonly ICategoriesApi _api;
        public IBroker Broker => _broker;
        private readonly IBroker _broker;

        public CategoriesService(IBroker broker) => (_broker) = (broker);
        public CategoriesService(IBroker broker, ICategoriesApi api) => (_broker, _api) = (broker, api);
    }
}
