using Posts.Provider.Abstractions;

namespace Posts.Provider
{
    public class PostsService(IBroker broker) : IPostsService
    {
        public IBroker Broker => broker;
    }
}
