using System.Security.Cryptography.X509Certificates;

namespace SecretProvider.Providers.Api;

/// <summary>
/// Settings for the mutually authenticated secret API.
/// Values may be supplied from appsettings, environment variables using the
/// <c>SecretProvider__*</c> prefix, or another configuration source.
/// </summary>
public sealed class SecretApiOptions
{
    public const string SectionName = "SecretProvider";

    /// <summary>
    /// Enables startup resolution. When omitted, the provider activates automatically
    /// when a base address is configured. Set false for an explicit opt-out.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Allows startup to continue with the original placeholders when the provider
    /// is unavailable. Keep false in production so required secrets fail fast.
    /// </summary>
    public bool Optional { get; set; }

    public string? BaseAddress { get; set; }

    /// <summary>
    /// Relative endpoint template. The namespace and key replacements are always
    /// percent-encoded before a request is sent.
    /// </summary>
    public string EndpointTemplate { get; set; } = "api/secrets/{namespace}/{key}";

    /// <summary>JSON property containing the secret value.</summary>
    public string ValuePropertyName { get; set; } = "value";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Total attempts for transient HTTP and network failures.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Maximum accepted JSON response size for one secret.</summary>
    public int MaxResponseBytes { get; set; } = 65_536;

    /// <summary>Permits HTTP only for isolated local development.</summary>
    public bool AllowInsecureHttp { get; set; }

    public MachineCertificateOptions Certificate { get; set; } = new();

    /// <summary>
    /// Maps an application-facing placeholder such as <c>{{secret:DatabaseConnectionString}}</c>
    /// to provider-owned coordinates. The mapping contains identifiers only, never secret values.
    /// </summary>
    public Dictionary<string, SecretAliasOptions> References { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    internal SecretReference ResolveAlias(string alias)
    {
        var mapping = References.FirstOrDefault(entry =>
            string.Equals(entry.Key, alias, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(mapping.Key) || mapping.Value is null)
        {
            throw new InvalidOperationException(
                $"{SectionName}:References does not define secret alias '{alias}'.");
        }

        return new SecretReference(mapping.Value.Namespace, mapping.Value.Key);
    }

    internal Uri ValidateAndGetBaseAddress()
    {
        if (!Uri.TryCreate(BaseAddress, UriKind.Absolute, out var baseAddress))
            throw new InvalidOperationException(
                $"{SectionName}:BaseAddress must be an absolute URI when the secret provider is enabled.");

        if (baseAddress.Scheme != Uri.UriSchemeHttps
            && !(AllowInsecureHttp && baseAddress.Scheme == Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                $"{SectionName}:BaseAddress must use HTTPS. " +
                $"Set {SectionName}:AllowInsecureHttp=true only for isolated local development.");
        }

        if (string.IsNullOrWhiteSpace(EndpointTemplate)
            || !EndpointTemplate.Contains("{namespace}", StringComparison.Ordinal)
            || !EndpointTemplate.Contains("{key}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SectionName}:EndpointTemplate must contain '{{namespace}}' and '{{key}}'.");
        }

        if (RequestTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}:RequestTimeout must be greater than zero.");

        if (MaxAttempts is < 1 or > 10)
            throw new InvalidOperationException($"{SectionName}:MaxAttempts must be between 1 and 10.");

        if (MaxResponseBytes is < 1 or > 1_048_576)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxResponseBytes must be between 1 and 1048576.");
        }

        if (string.IsNullOrWhiteSpace(ValuePropertyName))
            throw new InvalidOperationException($"{SectionName}:ValuePropertyName is required.");

        return baseAddress;
    }
}

/// <summary>Provider-owned coordinates for one logical application secret name.</summary>
public sealed class SecretAliasOptions
{
    public string Namespace { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Runtime roles authorized to fetch this secret. ApiHost role-scoped startup
    /// requires an explicit, non-empty list so a workload identity cannot acquire
    /// credentials for dependencies that process never uses.
    /// </summary>
    public string[] Roles { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Locates the client certificate either in the operating-system certificate store or in a
/// tightly permissioned PKCS#12 file mounted by the workload platform. File loading uses
/// ephemeral key storage so a read-only container never writes private-key material to disk.
/// </summary>
public sealed class MachineCertificateOptions
{
    /// <summary>
    /// Absolute path to a PKCS#12/PFX client certificate. This is the supported container
    /// mechanism. It is mutually exclusive with <see cref="Thumbprint"/> and
    /// <see cref="SubjectName"/>.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Optional PKCS#12 password supplied by bootstrap configuration (normally a workload
    /// secret/environment value). Secret placeholders are intentionally forbidden throughout
    /// the SecretProvider bootstrap section.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Preferred stable identifier. Spaces and casing are ignored when matching.
    /// </summary>
    public string? Thumbprint { get; set; }

    /// <summary>
    /// Subject distinguished name or simple subject name. Useful with certificate
    /// rotation when a fixed thumbprint is not suitable.
    /// </summary>
    public string? SubjectName { get; set; }

    public StoreName StoreName { get; set; } = StoreName.My;

    /// <summary>Defaults to the machine store as required for deployed services.</summary>
    public StoreLocation StoreLocation { get; set; } = StoreLocation.LocalMachine;

    public bool RequireClientAuthenticationEku { get; set; } = true;
}
