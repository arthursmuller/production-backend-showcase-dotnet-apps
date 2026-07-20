using FollowingProvider.Abstractions;

namespace FollowingProvider
{
    public class FollowingService : IFollowingService
    {
        public IFollowingApi Api => _api;
        private readonly IFollowingApi _api;
        public FollowingService(IFollowingApi api) => (_api) = (api);
    }
}
