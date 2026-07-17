using HttpClientHelper;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using FollowingProvider.Providers.Api;

namespace FollowingProvider.Abstractions
{
    public abstract class FollowingApiBase : HttpClientProviderBase
	{
		protected FollowingApiBase(HttpClient httpClient) : base(httpClient) { }

		protected void handleError<T>(HttpRequestException ex)
		{
			var statusCode = (int)ex.StatusCode;
			var message = $"{typeof(T).Name.Replace("Dto", "")} - ReqPayload " + "{0}";
			var responsePayload = (string)ex.Data["res_payload"];
			var reqPayload = ex.Data["req_payload"];
			var resPayload = fromJson<DefaultResponse<T>>(responsePayload);

			if (resPayload is not null)
				throw new FollowingProviderException(
					string.Format(" ---- Res Error Message: {1}  ---- " + message,
						reqPayload,
						handleResponseErrorMessage(resPayload.Errors),
					statusCode));

			throw new FollowingProviderException(string.Format(message, reqPayload), statusCode);
		}

		protected void handleError<T>(DefaultResponse<T> res)
		{
			var message = $"{typeof(T).Name} - " + "{0}";

			if (!res.Succeeded)
				throw new FollowingProviderException(string.Format(message, handleResponseErrorMessage(res.Errors)));
		}

		protected void handleError(HttpRequestException ex) 
		{
			var statusCode = (int)ex.StatusCode;
			var message = "ResPayload -- {0} ";
			var responsePayload = (string)ex.Data["res_payload"];

			throw new FollowingProviderException(string.Format(message, responsePayload), statusCode);
		}

		private string handleResponseErrorMessage(object err)
		{
			var res = new List<string>();

			if (err is null)
				return default;

			var type = err.GetType().Name.ToLower();

			if (type != nameof(JsonElement).ToLower())
				return default;

			var errorArr = fromJson<List<string>>(JsonSerializer.Serialize(err));
			var errorMsg = fromJson<string>(JsonSerializer.Serialize(err));

			if (errorArr is not null)
				res.AddRange(errorArr);
			if (errorMsg is not null)
				res.Add(errorMsg);

			return string.Join(";", res);
		}
	}
}
