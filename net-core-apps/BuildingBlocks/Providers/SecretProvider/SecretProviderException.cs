using System.Net;

namespace SecretProvider;

/// <summary>
/// Describes a secret-provider failure without including a secret value.
/// </summary>
public sealed class SecretProviderException : Exception
{
    public SecretProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public HttpStatusCode? StatusCode { get; init; }
}
