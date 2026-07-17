using System.Text.Json.Serialization;

namespace StorageProvider.Providers.Minio;

public class MinioStorageOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = null!;

    [JsonPropertyName("accessKey")]
    public string AccessKey { get; set; } = null!;

    [JsonPropertyName("secretKey")]
    public string SecretKey { get; set; } = null!;

    [JsonPropertyName("bucket")]
    public string BucketName { get; set; } = null!;

    [JsonPropertyName("useSSL")]
    public bool UseSSL { get; set; }

    [JsonPropertyName("publicUrl")]
    public string? PublicBaseUrl { get; set; }
}
