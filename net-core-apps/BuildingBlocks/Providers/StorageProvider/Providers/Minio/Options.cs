namespace StorageProvider.Providers.Minio;

public class MinioStorageOptions
{
    public const long DefaultMaximumUploadBytes = 32L * 1024 * 1024;

    public bool Enabled { get; set; }

    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public bool UseSSL { get; set; }

    public string? PublicUrl { get; set; }

    public bool CreateBucket { get; set; }

    public bool AllowPublicRead { get; set; }

    public long MaximumUploadBytes { get; set; } = DefaultMaximumUploadBytes;
}
