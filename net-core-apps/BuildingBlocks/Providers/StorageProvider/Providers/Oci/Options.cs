using System.Text.Json.Serialization;

namespace StorageProvider.Providers.Oci;

public class OciObjectStorageOptions
{
    public const string PublicAccessMode = "Public";
    public const string PrivateAccessMode = "Private";
    public const long DefaultMaximumUploadBytes = 32L * 1024 * 1024;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("authMode")]
    public string AuthMode { get; set; } = "ResourcePrincipal";

    [JsonPropertyName("tenancyOcid")]
    public string TenancyOcid { get; set; } = string.Empty;

    [JsonPropertyName("userOcid")]
    public string UserOcid { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("bucketName")]
    public string BucketName { get; set; } = string.Empty;

    [JsonPropertyName("privateKeyPath")]
    public string? PrivateKeyPath { get; set; }

    [JsonPropertyName("privateKeyPem")]
    public string? PrivateKeyPem { get; set; }

    [JsonPropertyName("privateKeyPassphrase")]
    public string? PrivateKeyPassphrase { get; set; }

    [JsonPropertyName("publicBaseUrl")]
    public string? PublicBaseUrl { get; set; }

    [JsonPropertyName("accessMode")]
    public string AccessMode { get; set; } = PublicAccessMode;

    [JsonPropertyName("maximumUploadBytes")]
    public long MaximumUploadBytes { get; set; } = DefaultMaximumUploadBytes;

    [JsonIgnore]
    public bool UsesPrivateAccess =>
        AccessMode.Equals(PrivateAccessMode, StringComparison.OrdinalIgnoreCase);
}
