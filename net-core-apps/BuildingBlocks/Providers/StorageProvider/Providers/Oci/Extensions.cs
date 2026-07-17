using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace StorageProvider.Providers.Oci;

public static class OciStorageProviderExtensions
{
    public static IServiceCollection ConfigureOciStorageProvider(
        this IServiceCollection services,
        OciObjectStorageOptions opts)
    {
        services.AddSingleton(opts);
        services.AddScoped<IStorageProvider, OciObjectStorageProvider>();
        return services;
    }

    public static OciObjectStorageOptions? BuildOptions()
    {
        var json = Environment.GetEnvironmentVariable("OCI_CONNECTION");
        if (string.IsNullOrWhiteSpace(json)) return null;

        return JsonSerializer.Deserialize<OciObjectStorageOptions>(json)
            ?? throw new InvalidOperationException("OCI_CONNECTION could not be deserialized.");
    }
}
