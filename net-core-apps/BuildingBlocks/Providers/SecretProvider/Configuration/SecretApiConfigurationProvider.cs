using Microsoft.Extensions.Configuration;
using SecretProvider.Providers.Api;

namespace SecretProvider.Configuration;

internal sealed class SecretApiConfigurationSource : IConfigurationSource
{
    private readonly SecretApiOptions _options;
    private readonly IReadOnlyDictionary<string, string> _placeholderValues;
    private readonly Func<SecretApiOptions, ISecretProvider> _providerFactory;

    public SecretApiConfigurationSource(
        SecretApiOptions options,
        IReadOnlyDictionary<string, string> placeholderValues,
        Func<SecretApiOptions, ISecretProvider>? providerFactory = null)
    {
        _options = options;
        _placeholderValues = placeholderValues;
        _providerFactory = providerFactory ?? (configuredOptions => new ApiSecretProvider(configuredOptions));
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SecretApiConfigurationProvider(_options, _placeholderValues, _providerFactory);
}

internal sealed class SecretApiConfigurationProvider : ConfigurationProvider
{
    private readonly SecretApiOptions _options;
    private readonly IReadOnlyDictionary<string, string> _placeholderValues;
    private readonly Func<SecretApiOptions, ISecretProvider> _providerFactory;
    private IReadOnlyDictionary<string, string?> _snapshot =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public SecretApiConfigurationProvider(
        SecretApiOptions options,
        IReadOnlyDictionary<string, string> placeholderValues,
        Func<SecretApiOptions, ISecretProvider> providerFactory)
    {
        _options = options;
        _placeholderValues = placeholderValues;
        _providerFactory = providerFactory;
    }

    public override void Load()
    {
        try
        {
            var provider = _providerFactory(_options);
            try
            {
                var references = _placeholderValues.Values
                    .SelectMany(value => SecretPlaceholder.GetReferences(value, _options))
                    .Distinct()
                    .ToArray();

                var resolvedSecrets = references.ToDictionary(
                    reference => reference,
                    reference => GetRequiredSecret(provider, reference));

                var resolvedSnapshot = _placeholderValues.ToDictionary(
                    entry => entry.Key,
                    entry => (string?)SecretPlaceholder.Resolve(
                        entry.Value,
                        _options,
                        resolvedSecrets),
                    StringComparer.OrdinalIgnoreCase);

                Volatile.Write(ref _snapshot, resolvedSnapshot);
                Data = resolvedSnapshot;
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }
        catch (Exception exception) when (_options.Optional)
        {
            // An explicitly optional provider contributes no values on failure. Lower-priority
            // configuration remains intact, including placeholders, so consumers can decide
            // whether an individual setting is required.
            var emptySnapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _snapshot, emptySnapshot);
            Data = emptySnapshot;
            System.Diagnostics.Trace.TraceWarning(
                "Optional secret provider could not resolve startup configuration: {0}",
                exception.Message);
        }
    }

    public override bool TryGet(string key, out string? value) =>
        Volatile.Read(ref _snapshot).TryGetValue(key, out value);

    private static string GetRequiredSecret(
        ISecretProvider provider,
        SecretReference reference)
    {
        var value = provider
            .GetSecretAsync(reference)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return string.IsNullOrWhiteSpace(value)
            ? throw new SecretProviderException(
                $"Secret provider returned an empty value for '{reference}'.")
            : value;
    }
}
