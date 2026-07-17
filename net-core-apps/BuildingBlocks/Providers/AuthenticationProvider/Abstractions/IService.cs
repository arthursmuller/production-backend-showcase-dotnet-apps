
using AuthenticationProvider.Providers.Api;

namespace AuthenticationProvider.Abstractions
{
    public interface IAuthService
    {
        public IAuthApi Api { get; }
    }
}
