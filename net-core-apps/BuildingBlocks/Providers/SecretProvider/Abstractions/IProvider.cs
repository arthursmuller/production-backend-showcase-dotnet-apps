namespace SecretProvider;

/// <summary>
/// Retrieves secrets without exposing transport or certificate details to consumers.
/// </summary>
public interface ISecretProvider
{
    Task<string> GetSecretAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default);
}
