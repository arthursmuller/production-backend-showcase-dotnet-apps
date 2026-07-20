using HttpClientHelper;
using System.Net.Http;

namespace AuthenticationProvider.Abstractions
{
    public abstract class AuthenticationApiBase : HttpClientProviderBase
    {
        protected AuthenticationApiBase(HttpClient httpClient) : base(httpClient) { }

        protected void handleError(HttpRequestException ex)
        {
            var statusCode = ex.StatusCode is null ? 0 : (int)ex.StatusCode;
            throw new AuthenticationProviderException("The identity service request failed.", statusCode);
        }
    }
}
