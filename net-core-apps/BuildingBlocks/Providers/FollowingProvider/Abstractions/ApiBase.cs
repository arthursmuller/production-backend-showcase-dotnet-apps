using HttpClientHelper;
using System.Net.Http;

namespace FollowingProvider.Abstractions
{
    public abstract class FollowingApiBase : HttpClientProviderBase
    {
        protected FollowingApiBase(HttpClient httpClient) : base(httpClient) { }

        protected void HandleError(HttpRequestException exception)
        {
            var statusCode = exception.StatusCode is null ? 0 : (int)exception.StatusCode;
            throw new FollowingProviderException(
                "The following service request failed.",
                statusCode);
        }
    }
}
