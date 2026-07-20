using HttpClientHelper;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace CategoriesProvider.Providers.Api
{
    public abstract class CategoriesApiBase : HttpClientProviderBase
    {
        protected CategoriesApiBase(HttpClient httpClient) : base(httpClient) { }

        protected void handleError<T>(DefaultResponse<T> res)
        {
            ArgumentNullException.ThrowIfNull(res);

            if (!res.Succeeded)
            {
                throw new CategoriesProviderException(
                    $"{typeof(T).Name} - {handleResponseErrorMessage(res.Errors)}");
            }
        }

        protected void handleError(HttpRequestException ex)
        {
            var statusCode = ex.StatusCode is null ? 0 : (int)ex.StatusCode;
            throw new CategoriesProviderException("The categories service request failed.", statusCode);
        }

        private static string handleResponseErrorMessage(object error)
        {
            if (error is null)
                return "The categories service returned an unspecified error.";
            if (error is string message)
                return message;
            if (error is string[] messages)
                return string.Join(";", messages);
            if (error is not JsonElement element)
                return error.ToString() ?? "The categories service returned an unspecified error.";

            return element.ValueKind switch
            {
                JsonValueKind.Array => string.Join(
                    ";",
                    element.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))),
                JsonValueKind.String => element.GetString()
                    ?? "The categories service returned an unspecified error.",
                _ => element.ToString(),
            };
        }
    }
}
