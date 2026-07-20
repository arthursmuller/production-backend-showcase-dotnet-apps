using Inspacy.Caching;
using Microsoft.Extensions.Options;
using Xunit;

namespace HybridCache.UnitTests;

public sealed class CacheKeyAndOptionsTests
{
    [Fact]
    public void Key_contains_canonical_namespace_and_hashes_entity_and_tenant_identifiers()
    {
        var options = Options.Create(new InspacyHybridCacheOptions
        {
            MaximumKeyLength = 256,
        });
        var factory = new CacheKeyFactory(
            new CacheNamespace("Production", "Feed.API"),
            options);

        var key = factory.Create(
            "first-page",
            "user@example.test",
            entityVersion: 91,
            tenantId: "customer-alpha");

        Assert.StartsWith("inspacy:production:feed.api:tenant-", key.Value);
        Assert.Contains(":first-page:id-", key.Value);
        Assert.EndsWith(":e91", key.Value);
        Assert.DoesNotContain("user@example.test", key.Value);
        Assert.DoesNotContain("customer-alpha", key.Value);
        Assert.True(key.Value.Length <= options.Value.MaximumKeyLength);
    }

    [Fact]
    public void Key_uses_explicit_global_segment_when_tenancy_does_not_apply()
    {
        var factory = new CacheKeyFactory(
            new CacheNamespace("development", "comment-api"),
            Options.Create(new InspacyHybridCacheOptions()));

        var key = factory.Create("comment-card", Guid.Empty.ToString(), 0);

        Assert.StartsWith("inspacy:development:comment-api:global:comment-card:", key.Value);
    }

    [Theory]
    [InlineData(0, 15, 256, 262144, 64, 50, false)]
    [InlineData(60, 61, 256, 262144, 64, 50, false)]
    [InlineData(60, 15, 191, 262144, 64, 50, false)]
    [InlineData(60, 15, 256, 1048577, 64, 50, false)]
    [InlineData(60, 15, 256, 262144, 0, 50, false)]
    [InlineData(60, 15, 256, 262144, 64, 0, false)]
    [InlineData(60, 15, 256, 262144, 64, 50, true)]
    public void Validator_rejects_unbounded_or_incomplete_configuration(
        int expiration,
        int localExpiration,
        int keyLength,
        long payloadBytes,
        int concurrency,
        int queueTimeout,
        bool redisEnabled)
    {
        var options = new InspacyHybridCacheOptions
        {
            DefaultExpirationSeconds = expiration,
            DefaultLocalExpirationSeconds = localExpiration,
            MaximumKeyLength = keyLength,
            MaximumPayloadBytes = payloadBytes,
            SourceConcurrencyLimit = concurrency,
            SourceQueueTimeoutMilliseconds = queueTimeout,
            Redis = new InspacyRedisCacheOptions
            {
                Enabled = redisEnabled,
                ConnectionString = null,
            },
        };

        var result = new InspacyHybridCacheOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validator_accepts_bounded_optional_redis_configuration()
    {
        var options = new InspacyHybridCacheOptions
        {
            Redis = new InspacyRedisCacheOptions
            {
                Enabled = true,
                ConnectionString = "redis.internal:6379,abortConnect=false",
            },
        };

        var result = new InspacyHybridCacheOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_reports_null_redis_object_and_instance_name_without_throwing()
    {
        var validator = new InspacyHybridCacheOptionsValidator();
        var missingRedis = new InspacyHybridCacheOptions { Redis = null! };
        var missingInstance = new InspacyHybridCacheOptions
        {
            Redis = new InspacyRedisCacheOptions { InstanceName = null! },
        };

        Assert.False(validator.Validate(null, missingRedis).Succeeded);
        Assert.False(validator.Validate(null, missingInstance).Succeeded);
    }
}
