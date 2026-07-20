using System.Threading.Tasks;
using System.Threading;

namespace AuthenticationProvider.Providers.Api
{
    public interface IAuthApi
    {
        Task<string> GetAccessToken(
            string clientId,
            string clientSecret,
            string[] scopes,
            CancellationToken cancellationToken);
    }
}
