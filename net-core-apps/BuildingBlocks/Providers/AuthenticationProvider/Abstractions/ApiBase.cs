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
			var message = "ResPayload -- {0} ";
			var responsePayload = ex.Data["res_payload"]?.ToString() ?? ex.Message;

			throw new AuthenticationProviderException(string.Format(message, responsePayload), statusCode);
		}
	}
}
