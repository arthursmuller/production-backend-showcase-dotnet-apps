using AuthenticationProvider.Abstractions;
using AuthenticationProvider.Providers.Api;

namespace AuthenticationProvider
{
    public class AuthenticationService : IAuthService
    {
        public IAuthApi Api => _api;
        private readonly IAuthApi _api;
        public AuthenticationService(IAuthApi api) => (_api) =  (api);
    }
}
