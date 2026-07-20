using Microsoft.Extensions.Configuration;
using SecretProvider.Configuration;
using SecretProvider.Providers.Api;

namespace SecretProvider;

public static class SecretProviderExtensions
{
    /// <summary>
    /// Resolves all effective configuration values containing placeholders such as
    /// <c>{{secret:FeedClientSecret}}</c>. Environment variables and
    /// command-line values retain their normal precedence, so a non-placeholder override
    /// is never sent to the secret API.
    /// </summary>
    public static ConfigurationManager AddSecretApiProvider(
        this ConfigurationManager configuration,
        string? runtimeRole = null,
        string? environmentName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(SecretApiOptions.SectionName)
            .Get<SecretApiOptions>() ?? new SecretApiOptions();

        var markerEntries = configuration.AsEnumerable()
            .Where(entry => entry.Value is not null
                && SecretPlaceholder.ContainsReservedMarker(entry.Value))
            .ToArray();

        var bootstrapPlaceholder = markerEntries.FirstOrDefault(entry => entry.Key.StartsWith(
            SecretApiOptions.SectionName + ConfigurationPath.KeyDelimiter,
            StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(bootstrapPlaceholder.Key))
        {
            throw new InvalidOperationException(
                $"{SecretApiOptions.SectionName} bootstrap configuration cannot contain secret placeholders.");
        }

        foreach (var entry in markerEntries)
            SecretPlaceholder.Validate(entry.Value!, options);

        var placeholders = SelectRolePlaceholders(markerEntries, options, runtimeRole)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!,
                StringComparer.OrdinalIgnoreCase);

        if (placeholders.Count == 0)
            return configuration;

        var effectiveEnvironmentName = environmentName
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"];
        var isProduction = string.IsNullOrWhiteSpace(effectiveEnvironmentName)
            || string.Equals(effectiveEnvironmentName, "Production", StringComparison.OrdinalIgnoreCase);

        if (options.AllowInsecureHttp && isProduction)
        {
            throw new InvalidOperationException(
                $"{SecretApiOptions.SectionName}:AllowInsecureHttp can only be enabled outside production.");
        }

        if (options.Optional && isProduction)
        {
            throw new InvalidOperationException(
                $"{SecretApiOptions.SectionName}:Optional can only be enabled outside production.");
        }

        if (options.Enabled == false)
        {
            if (isProduction)
            {
                throw new InvalidOperationException(
                    "Unresolved secret placeholders were found while the production secret provider is disabled.");
            }

            return configuration;
        }

        if (string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            if (isProduction || options.Enabled == true)
            {
                throw new InvalidOperationException(
                    "Unresolved secret placeholders were found, " +
                    $"but {SecretApiOptions.SectionName}:BaseAddress is not configured. " +
                    "Configure the secret API or explicitly override every placeholder.");
            }

            return configuration;
        }

        ((IConfigurationBuilder)configuration).Add(
            new SecretApiConfigurationSource(options, placeholders));
        return configuration;
    }

    internal static IEnumerable<KeyValuePair<string, string?>> SelectRolePlaceholders(
        IEnumerable<KeyValuePair<string, string?>> markerEntries,
        SecretApiOptions options,
        string? runtimeRole)
    {
        ArgumentNullException.ThrowIfNull(markerEntries);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(runtimeRole))
            return markerEntries;

        var normalizedRole = NormalizeRuntimeRole(runtimeRole);
        return markerEntries.Where(entry =>
        {
            var references = SecretPlaceholder.GetReferences(entry.Value!, options);
            return references.All(reference =>
                IsReferenceAuthorized(options, reference, normalizedRole));
        });
    }

    private static bool IsReferenceAuthorized(
        SecretApiOptions options,
        SecretReference reference,
        string runtimeRole)
    {
        var aliases = options.References
            .Where(entry => entry.Value is not null
                && string.Equals(
                    entry.Value.Namespace,
                    reference.Namespace,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    entry.Value.Key,
                    reference.Key,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (aliases.Length == 0)
        {
            throw new InvalidOperationException(
                "Role-scoped secret resolution requires every placeholder reference to use an alias with an explicit Roles allow-list.");
        }

        foreach (var alias in aliases)
        {
            if (alias.Value.Roles is null || alias.Value.Roles.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{SecretApiOptions.SectionName}:References:{alias.Key}:Roles must explicitly authorize at least one runtime role.");
            }

            foreach (var configuredRole in alias.Value.Roles)
                _ = NormalizeRuntimeRole(configuredRole);
        }

        return aliases.Any(alias => alias.Value.Roles.Any(role =>
            string.Equals(
                NormalizeRuntimeRole(role),
                runtimeRole,
                StringComparison.Ordinal)));
    }

    private static string NormalizeRuntimeRole(string runtimeRole) =>
        runtimeRole.Trim().ToUpperInvariant() switch
        {
            "API" => "Api",
            "CONSUMERWORKER" => "ConsumerWorker",
            "JOBWORKER" => "JobWorker",
            "MIGRATION" => "Migration",
            "COMBINED" => "Combined",
            _ => throw new InvalidOperationException(
                $"Unsupported runtime role '{runtimeRole}' for role-scoped secret resolution."),
        };
}
