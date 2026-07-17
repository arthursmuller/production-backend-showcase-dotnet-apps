using System.Threading.Tasks;

namespace AuthenticationProvider.Providers.Api
{
    public interface IAuthApi
    {
        Task<string> GetAccessToken(string clientId, string clientSecret, string[] scopes);
    }
}
