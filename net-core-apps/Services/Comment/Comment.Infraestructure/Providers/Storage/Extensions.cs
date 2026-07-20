using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Comment.Application.Providers.Storage;
using InspacyApiHost;
using StorageProvider;
using StorageProvider.Providers.Minio;
using StorageProvider.Providers.Oci;
using System;
using System.Linq;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Comment.Infra.Providers.Storage
{
    public static class StorageExtensions
    {
        private const int MaxContainerNameLength = 128;

        public static IServiceCollection ConfigureStorage(this IServiceCollection services, IConfiguration configuration)
        {
            var runtimeRole = RuntimeIdentity.Resolve(configuration);
            var runsMediaWorkflow = MediaSecurityReadinessHealthCheck
                .RunsMediaWorkflow(runtimeRole);
            if (!runsMediaWorkflow)
                return services;

            ConfigureStorageVariables(services, configuration);
            RequirePrivateObjectStorage(configuration);

            var mediaSecurity = configuration
                .GetSection(MediaSecurityOptions.SectionName)
                .Get<MediaSecurityOptions>() ?? new MediaSecurityOptions();
            var environment = configuration["ASPNETCORE_ENVIRONMENT"]
                ?? configuration["DOTNET_ENVIRONMENT"]
                ?? "Production";
            if (!mediaSecurity.IsValid(environment))
            {
                throw new InvalidOperationException(
                    $"{MediaSecurityOptions.SectionName} is invalid. Non-development API roles " +
                    "must use a configured fail-closed malware scanner.");
            }

            services.AddSingleton(mediaSecurity);
            services.AddSingleton<IClamAvClient, ClamAvClient>();
            services.AddSingleton<ClamAvResiliencePolicy>();
            services.AddSingleton<MediaSecurityScanner>();
            services.AddSingleton<IMediaSecurityScanner>(provider =>
                provider.GetRequiredService<MediaSecurityScanner>());
            services.AddHealthChecks().AddCheck<MediaSecurityReadinessHealthCheck>(
                "comment-media-security",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(
                    mediaSecurity.ReadinessProbeTimeoutSeconds + 1));
            services.ConfigureStorageProvider(configuration);
            services.AddScoped<ICommentMediaObjectStore, Storage>();

            return services;
        }

        private static void RequirePrivateObjectStorage(IConfiguration configuration)
        {
            var oci = OciStorageProviderExtensions.BuildOptions(configuration);
            if (oci is { Enabled: true })
            {
                if (!oci.UsesPrivateAccess)
                {
                    throw new InvalidOperationException(
                        "Comment media requires Storage:OciConnection accessMode Private and a non-public bucket.");
                }
                return;
            }

            var minio = MinioStorageProviderExtensions.BuildOptions(configuration);
            if (minio is { Enabled: true, AllowPublicRead: true })
            {
                throw new InvalidOperationException(
                    "Comment media cannot use a publicly readable MinIO bucket.");
            }
        }

        private static IServiceCollection ConfigureStorageVariables(IServiceCollection services, IConfiguration configuration)
        {
            var config = new StorageConfiguration();
            var propertyNames = typeof(StorageConfiguration).GetProperties().Select(property => property.Name);

            foreach (var propertyName in propertyNames)
            {
                var propNameWithUderScoreBeforeEachUpper = string.Join("", propertyName.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c.ToString() : c.ToString()));
                var envVarToSearch = $"STORAGE_CONTAINER{propNameWithUderScoreBeforeEachUpper}";
                var defaultValue = propertyName.ToLower();
                var settingValue = configuration[envVarToSearch] ?? defaultValue;
                if (string.IsNullOrWhiteSpace(settingValue)
                    || settingValue.Length > MaxContainerNameLength
                    || settingValue.Any(char.IsControl)
                    || settingValue.Contains("{{secret", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Storage container setting '{envVarToSearch}' is invalid.");
                }

                typeof(StorageConfiguration).GetProperty(propertyName)?.SetValue(config, settingValue);
            }

            services.AddSingleton(config);

            return services;
        }
    }
}

