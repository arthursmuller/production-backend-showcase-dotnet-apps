using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace StorageProvider.Providers.Minio;

public static class MinioStorageProviderExtensions
{
    public static IServiceCollection ConfigureMinioStorageProvider(
        this IServiceCollection services,
        MinioStorageOptions opts)
    {
        services.AddSingleton(opts);
        services.AddScoped<IStorageProvider, MinioStorageProvider>();
        return services;
    }

    public static MinioStorageOptions? BuildOptions()
    {
        var json = Environment.GetEnvironmentVariable("MINIO_CONNECTION");
        if (string.IsNullOrWhiteSpace(json)) return null;

        var opts = JsonSerializer.Deserialize<MinioStorageOptions>(json)
            ?? throw new InvalidOperationException("MINIO_CONNECTION could not be deserialized.");

        if (string.IsNullOrWhiteSpace(opts.PublicBaseUrl))
            opts.PublicBaseUrl = $"{(opts.UseSSL ? "https" : "http")}://{opts.Endpoint}";

        return opts;
    }
}
