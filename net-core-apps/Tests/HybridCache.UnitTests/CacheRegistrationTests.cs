using Inspacy.Caching;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HybridCache.UnitTests;

public sealed class CacheRegistrationTests
{
    [Fact]
    public async Task Registration_overrides_connection_string_timeouts_with_validated_bounds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Enabled"] = "true",
                ["Cache:Redis:ConnectionString"] = "redis.internal:6379,abortConnect=true,connectTimeout=9999,syncTimeout=9999,asyncTimeout=9999",
                ["Cache:Redis:ConnectTimeoutMilliseconds"] = "800",
                ["Cache:Redis:OperationTimeoutMilliseconds"] = "175",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInspacyHybridCache(configuration, "production", "feed.api");
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var redis = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        Assert.NotNull(redis.ConfigurationOptions);
        Assert.False(redis.ConfigurationOptions.AbortOnConnectFail);
        Assert.Equal(1, redis.ConfigurationOptions.ConnectRetry);
        Assert.Equal(800, redis.ConfigurationOptions.ConnectTimeout);
        Assert.Equal(175, redis.ConfigurationOptions.SyncTimeout);
        Assert.Equal(175, redis.ConfigurationOptions.AsyncTimeout);
    }
}
