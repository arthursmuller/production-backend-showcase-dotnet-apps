using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using InspacyApiHost;
using StackExchange.Redis;

namespace Inspacy.Caching;

public static class HybridCacheServiceCollectionExtensions
{
    public static IServiceCollection AddInspacyHybridCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.AddOptions<InspacyHybridCacheOptions>()
            .Bind(configuration.GetSection(InspacyHybridCacheOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<InspacyHybridCacheOptions>, InspacyHybridCacheOptionsValidator>();
        services.AddSingleton(new CacheNamespace(environmentName, serviceName));

        services.AddOptions<RedisCacheOptions>()
            .Configure<IOptions<InspacyHybridCacheOptions>>((redis, shared) =>
            {
                var cache = shared.Value.Redis;
                redis.InstanceName = cache.InstanceName;
                if (!cache.Enabled)
                    return;

                var stackExchange = ConfigurationOptions.Parse(
                    cache.ConnectionString!,
                    ignoreUnknown: false);
                stackExchange.AbortOnConnectFail = false;
                stackExchange.ConnectRetry = 1;
                stackExchange.ConnectTimeout = cache.ConnectTimeoutMilliseconds;
                stackExchange.SyncTimeout = cache.OperationTimeoutMilliseconds;
                stackExchange.AsyncTimeout = cache.OperationTimeoutMilliseconds;
                redis.ConfigurationOptions = stackExchange;
            });

        services.AddSingleton<RedisCache>();
        services.AddPlatformTimeProvider();
        services.AddSingleton<CacheAvailabilityState>();
        services.AddSingleton<ResilientDistributedCache>(serviceProvider =>
            new ResilientDistributedCache(
                serviceProvider.GetRequiredService<RedisCache>(),
                serviceProvider.GetRequiredService<IOptions<InspacyHybridCacheOptions>>(),
                serviceProvider.GetRequiredService<CacheAvailabilityState>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientDistributedCache>>()));
        services.AddSingleton<IDistributedCache>(serviceProvider =>
            serviceProvider.GetRequiredService<ResilientDistributedCache>());

        services.AddHybridCache();
        services.AddOptions<HybridCacheOptions>()
            .Configure<IOptions<InspacyHybridCacheOptions>>((hybrid, shared) =>
            {
                var cache = shared.Value;
                hybrid.MaximumKeyLength = cache.MaximumKeyLength;
                hybrid.MaximumPayloadBytes = cache.MaximumPayloadBytes;
                hybrid.ReportTagMetrics = false;
                hybrid.DefaultEntryOptions = new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromSeconds(cache.DefaultExpirationSeconds),
                    LocalCacheExpiration = TimeSpan.FromSeconds(cache.DefaultLocalExpirationSeconds),
                };
            });

        services.AddSingleton<ICacheKeyFactory, CacheKeyFactory>();
        services.AddSingleton<ISharedCache, SharedHybridCache>();
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "redis-cache",
            serviceProvider => serviceProvider.GetRequiredService<OptionalRedisHealthCheck>(),
            HealthStatus.Degraded,
            tags: new[] { "ready", "cache", "optional" }));
        services.AddSingleton<OptionalRedisHealthCheck>();

        return services;
    }
}
