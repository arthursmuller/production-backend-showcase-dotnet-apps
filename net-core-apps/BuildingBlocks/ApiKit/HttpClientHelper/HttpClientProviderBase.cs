using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HttpClientHelper;

public abstract class HttpClientProviderBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected readonly HttpClient _httpClient;

    protected HttpClientProviderBase(HttpClient httpClient) =>
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    protected async Task<T> getResponseAsync<T>(
        HttpResponseMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using (message)
        {
            try
            {
                message.EnsureSuccessStatusCode();
                await using var content = await message.Content.ReadAsStreamAsync(cancellationToken);
                var response = await JsonSerializer.DeserializeAsync<T>(
                    content,
                    SerializerOptions,
                    cancellationToken);
                return response ?? throw new JsonException(
                    $"The dependency returned JSON null instead of {typeof(T).Name}.");
            }
            catch (Exception exception)
            {
                // Status is safe operational metadata. Request and response bodies must
                // never be copied into Exception.Data, where exception logging would
                // persist credentials, tokens, or user content.
                exception.Data["status_code"] = message.StatusCode;
                throw;
            }
        }
    }
}
