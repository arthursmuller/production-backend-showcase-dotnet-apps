namespace CategoriesProvider.Abstractions
{
    public interface ICategoriesService
    {
        public IBroker Broker { get; }
        public ICategoriesApi Api { get; }
    }
}
