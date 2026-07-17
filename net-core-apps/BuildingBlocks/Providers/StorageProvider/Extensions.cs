using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StorageProvider.Providers.Minio;
using StorageProvider.Providers.Oci;

namespace StorageProvider;

public static class StorageProviderExtensions
{
    public static IServiceCollection ConfigureStorageProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var ociConfig = OciStorageProviderExtensions.BuildOptions();
        var minioConfig = MinioStorageProviderExtensions.BuildOptions();

        if (ociConfig is { Enabled: true })
        {
            services.ConfigureOciStorageProvider(ociConfig);
        }
        else if (minioConfig is { Enabled: true })
        {
            services.ConfigureMinioStorageProvider(minioConfig);
        }
        else
        {
            throw new InvalidOperationException(
                "No storage backend enabled. Set MINIO_CONNECTION (local dev) or OCI_CONNECTION (production) with \"enabled\":true.");
        }

        services.AddScoped<IStorageProviderService, StorageProviderService>();

        return services;
    }
}
