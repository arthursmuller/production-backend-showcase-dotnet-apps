using System.Text.Json.Serialization;

namespace StorageProvider.Providers.Oci;

public class OciObjectStorageOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("tenancyOcid")]
    public string TenancyOcid { get; set; } = null!;

    [JsonPropertyName("userOcid")]
    public string UserOcid { get; set; } = null!;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = null!;

    [JsonPropertyName("region")]
    public string Region { get; set; } = null!;

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = null!;

    [JsonPropertyName("bucketName")]
    public string BucketName { get; set; } = null!;

    [JsonPropertyName("privateKeyPath")]
    public string? PrivateKeyPath { get; set; }

    [JsonPropertyName("privateKeyPem")]
    public string? PrivateKeyPem { get; set; }

    [JsonPropertyName("privateKeyPassphrase")]
    public string? PrivateKeyPassphrase { get; set; }

    [JsonPropertyName("publicBaseUrl")]
    public string? PublicBaseUrl { get; set; }
}
