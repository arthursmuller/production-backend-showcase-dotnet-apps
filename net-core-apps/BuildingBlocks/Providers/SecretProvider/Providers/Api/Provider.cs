using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace SecretProvider.Providers.Api;

/// <summary>
/// Retrieves secrets over a mutually authenticated HTTPS connection.
/// </summary>
public sealed class ApiSecretProvider : ISecretProvider, IDisposable
{
    private readonly SecretApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly X509Certificate2 _certificate;
    private bool _disposed;

    public ApiSecretProvider(SecretApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var baseAddress = options.ValidateAndGetBaseAddress();
        _certificate = MachineCertificateLoader.Load(options.Certificate);

        var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = true,
            ClientCertificateOptions = ClientCertificateOption.Manual,
        };
        handler.ClientCertificates.Add(_certificate);

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        _options = options;
    }

    public async Task<string> GetSecretAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(reference);

        Exception? lastException = null;
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildEndpoint(reference));
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return await ReadSecretValueAsync(response, reference, timeout.Token).ConfigureAwait(false);

                var exception = new SecretProviderException(
                    $"Secret API returned HTTP {(int)response.StatusCode} while retrieving '{reference}'.")
                {
                    StatusCode = response.StatusCode,
                };

                if (!IsTransient(response.StatusCode) || attempt == _options.MaxAttempts)
                    throw exception;

                lastException = exception;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException(
                    $"Secret API request for '{reference}' exceeded {_options.RequestTimeout}.",
                    exception);
                if (attempt == _options.MaxAttempts)
                    break;
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
                if (attempt == _options.MaxAttempts)
                    break;
            }

            await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
        }

        throw new SecretProviderException(
            $"Secret API request for '{reference}' failed after {_options.MaxAttempts} attempts.",
            lastException);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _httpClient.Dispose();
        _certificate.Dispose();
        _disposed = true;
    }

    private string BuildEndpoint(SecretReference reference) =>
        _options.EndpointTemplate
            .TrimStart('/')
            .Replace(
                "{namespace}",
                Uri.EscapeDataString(reference.Namespace),
                StringComparison.Ordinal)
            .Replace(
                "{key}",
                Uri.EscapeDataString(reference.Key),
                StringComparison.Ordinal);

    private async Task<string> ReadSecretValueAsync(
        HttpResponseMessage response,
        SecretReference reference,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
            throw OversizedResponse(reference);

        using var bufferedContent = new MemoryStream(
            capacity: Math.Min(
                _options.MaxResponseBytes,
                (int)(response.Content.Headers.ContentLength ?? 0)));
        var buffer = new byte[Math.Min(8192, _options.MaxResponseBytes + 1)];
        while (true)
        {
            var bytesRead = await stream
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
                break;

            if (bufferedContent.Length + bytesRead > _options.MaxResponseBytes)
                throw OversizedResponse(reference);

            await bufferedContent
                .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);
        }

        bufferedContent.Position = 0;

        try
        {
            using var document = await JsonDocument
                .ParseAsync(bufferedContent, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (document.RootElement.ValueKind == JsonValueKind.String)
                return RequireValue(document.RootElement.GetString(), reference);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(_options.ValuePropertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return RequireValue(value.GetString(), reference);
            }
        }
        catch (JsonException exception)
        {
            throw new SecretProviderException(
                $"Secret API response for '{reference}' was not valid JSON.",
                exception);
        }

        throw new SecretProviderException(
            $"Secret API response for '{reference}' did not contain a string " +
            $"'{_options.ValuePropertyName}' property.");
    }

    private static string RequireValue(string? value, SecretReference reference) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new SecretProviderException(
                $"Secret API returned an empty value for '{reference}'.")
            : value;

    private SecretProviderException OversizedResponse(SecretReference reference) =>
        new($"Secret API response for '{reference}' exceeded {_options.MaxResponseBytes} bytes.");

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var exponentialMilliseconds = Math.Min(2000, 200 * (1 << Math.Min(attempt - 1, 4)));
        var jitterMilliseconds = Random.Shared.Next(25, 151);
        return Task.Delay(exponentialMilliseconds + jitterMilliseconds, cancellationToken);
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + '/', UriKind.Absolute);
}
