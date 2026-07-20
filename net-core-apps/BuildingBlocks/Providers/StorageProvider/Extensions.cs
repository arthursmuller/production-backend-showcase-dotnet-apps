using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StorageProvider.Providers.Minio;
using StorageProvider.Providers.Oci;

namespace StorageProvider;

public static class StorageProviderExtensions
{
    public static IServiceCollection ConfigureStorageProvider(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var ociConfig = OciStorageProviderExtensions.BuildOptions(configuration);
        var minioConfig = MinioStorageProviderExtensions.BuildOptions(configuration);

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
                "No storage backend enabled. Set Storage:Minio (local development) " +
                "or Storage:OciConnection (production) with \"enabled\":true.");
        }

        services.AddScoped<IStorageProviderService, StorageProviderService>();

        return services;
    }
}
