using UserProvider.Abstractions;

namespace UserProvider
{
    public class UserService : IUserService
    {
        public IBroker Broker => _broker;

        private readonly IBroker _broker;
        public UserService(IBroker broker) => (_broker) = (broker);
    }
}
