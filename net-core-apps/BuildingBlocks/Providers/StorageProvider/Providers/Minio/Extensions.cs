using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;

namespace StorageProvider.Providers.Minio;

public static class MinioStorageProviderExtensions
{
    private const string SectionName = "Storage:Minio";

    public static IServiceCollection ConfigureMinioStorageProvider(
        this IServiceCollection services,
        MinioStorageOptions opts)
    {
        services.AddSingleton(opts);
        services.AddSingleton<IMinioClient>(_ => new MinioClient()
            .WithEndpoint(opts.Endpoint)
            .WithCredentials(opts.AccessKey, opts.SecretKey)
            .WithSSL(opts.UseSSL)
            .Build());
        services.AddScoped<IStorageProvider, MinioStorageProvider>();
        return services;
    }

    public static MinioStorageOptions? BuildOptions(IConfiguration? configuration = null)
    {
        var enabledValue = configuration?[$"{SectionName}:enabled"];
        if (string.IsNullOrWhiteSpace(enabledValue))
            return null;

        if (!bool.TryParse(enabledValue, out var enabled))
            throw InvalidSetting("enabled", "must be true or false");

        var opts = new MinioStorageOptions { Enabled = enabled };
        if (!enabled)
            return opts;

        opts.Endpoint = Required(configuration!, "endpoint");
        opts.AccessKey = Required(configuration!, "accessKey");
        opts.SecretKey = Required(configuration!, "secretKey");
        opts.Bucket = Required(configuration!, "bucket");

        var useSslValue = Required(configuration!, "useSSL");
        if (!bool.TryParse(useSslValue, out var useSsl))
            throw InvalidSetting("useSSL", "must be true or false");

        opts.UseSSL = useSsl;
        opts.PublicUrl = configuration![$"{SectionName}:publicUrl"];
        if (string.IsNullOrWhiteSpace(opts.PublicUrl))
            opts.PublicUrl = $"{(opts.UseSSL ? "https" : "http")}://{opts.Endpoint}";

        opts.CreateBucket = OptionalBoolean(configuration!, "createBucket");
        opts.AllowPublicRead = OptionalBoolean(configuration!, "allowPublicRead");
        opts.MaximumUploadBytes = OptionalMaximumBytes(configuration!);

        var environment = configuration!["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Production";
        var isDevelopment = environment.Equals("Development", StringComparison.OrdinalIgnoreCase);
        if (!isDevelopment && (!opts.UseSSL || opts.CreateBucket || opts.AllowPublicRead))
        {
            throw new InvalidOperationException(
                $"{SectionName} must use TLS and cannot create or publish buckets outside Development.");
        }
        if (!Uri.TryCreate(opts.PublicUrl, UriKind.Absolute, out var publicUrl)
            || (!isDevelopment && !publicUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(publicUrl.UserInfo)
            || !string.IsNullOrEmpty(publicUrl.Query)
            || !string.IsNullOrEmpty(publicUrl.Fragment))
        {
            throw InvalidSetting("publicUrl", "must be a clean absolute HTTPS URL outside Development");
        }

        return opts;
    }

    private static bool OptionalBoolean(IConfiguration configuration, string key)
    {
        var value = configuration[$"{SectionName}:{key}"];
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return bool.TryParse(value, out var parsed)
            ? parsed
            : throw InvalidSetting(key, "must be true or false");
    }

    private static long OptionalMaximumBytes(IConfiguration configuration)
    {
        var value = configuration[$"{SectionName}:maximumUploadBytes"];
        if (string.IsNullOrWhiteSpace(value))
            return MinioStorageOptions.DefaultMaximumUploadBytes;
        if (!long.TryParse(value, out var parsed) || parsed is < 1 or > 100L * 1024 * 1024)
            throw InvalidSetting("maximumUploadBytes", "must be between 1 and 104857600");
        return parsed;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[$"{SectionName}:{key}"];
        if (string.IsNullOrWhiteSpace(value) || IsUnresolvedSecret(value))
            throw InvalidSetting(key, "is required when MinIO is enabled");

        return value;
    }

    private static InvalidOperationException InvalidSetting(string key, string reason) =>
        new($"{SectionName}:{key} {reason}.");

    private static bool IsUnresolvedSecret(string value) =>
        value.Contains("{{secret", StringComparison.Ordinal);
}
