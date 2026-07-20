using AuthenticationProvider.Dtos;
using AuthenticationProvider.Providers.Api;
using Xunit;

namespace AuthenticationProvider.UnitTests;

public sealed class AccessTokenCacheTests
{
    [Fact]
    public async Task Concurrent_requests_for_the_same_credential_are_coalesced()
    {
        var cache = new AccessTokenCache(TimeProvider.System);
        var acquisitionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcquisition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitionCount = 0;

        async Task<TokenResponse> Acquire(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref acquisitionCount);
            acquisitionStarted.TrySetResult();
            await releaseAcquisition.Task.WaitAsync(cancellationToken);
            return ValidToken("shared-token");
        }

        var requests = Enumerable.Range(0, 32)
            .Select(_ => cache.GetOrCreateAsync(
                "feed-client",
                "secret-value",
                ["following.read"],
                Acquire,
                TestContext.Current.CancellationToken))
            .ToArray();

        await acquisitionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        releaseAcquisition.TrySetResult();

        var tokens = await Task.WhenAll(requests);

        Assert.Equal(1, acquisitionCount);
        Assert.All(tokens, token => Assert.Equal("shared-token", token));
    }

    [Fact]
    public async Task Cache_key_normalizes_scope_order_but_isolates_different_scope_sets()
    {
        var cache = new AccessTokenCache(TimeProvider.System);
        var acquisitionCount = 0;

        Task<TokenResponse> Acquire(CancellationToken _)
        {
            var number = Interlocked.Increment(ref acquisitionCount);
            return Task.FromResult(ValidToken($"token-{number}"));
        }

        var first = await cache.GetOrCreateAsync(
            "client",
            "secret",
            ["feed.read", "following.read"],
            Acquire,
            TestContext.Current.CancellationToken);
        var reordered = await cache.GetOrCreateAsync(
            "client",
            "secret",
            [" following.read ", "feed.read", "feed.read"],
            Acquire,
            TestContext.Current.CancellationToken);
        var different = await cache.GetOrCreateAsync(
            "client",
            "secret",
            ["comment.read"],
            Acquire,
            TestContext.Current.CancellationToken);

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, different);
        Assert.Equal(2, acquisitionCount);
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_cancel_the_shared_acquisition_or_poison_the_cache()
    {
        var cache = new AccessTokenCache(TimeProvider.System);
        var acquisitionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcquisition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitionCount = 0;

        async Task<TokenResponse> Acquire(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref acquisitionCount);
            acquisitionStarted.TrySetResult();
            await releaseAcquisition.Task.WaitAsync(cancellationToken);
            return ValidToken("survived-token");
        }

        var owner = cache.GetOrCreateAsync(
            "client",
            "secret",
            ["read"],
            Acquire,
            TestContext.Current.CancellationToken);
        await acquisitionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        using var waiterCancellation = new CancellationTokenSource();
        var waiter = cache.GetOrCreateAsync(
            "client",
            "secret",
            ["read"],
            Acquire,
            waiterCancellation.Token);
        waiterCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        releaseAcquisition.TrySetResult();
        Assert.Equal("survived-token", await owner);

        var cached = await cache.GetOrCreateAsync(
            "client",
            "secret",
            ["read"],
            Acquire,
            TestContext.Current.CancellationToken);
        Assert.Equal("survived-token", cached);
        Assert.Equal(1, acquisitionCount);
    }

    [Fact]
    public async Task Client_key_and_secret_rotation_never_reuses_the_previous_credential_token()
    {
        var cache = new AccessTokenCache(TimeProvider.System);
        var acquisitionCount = 0;

        Task<TokenResponse> Acquire(CancellationToken _)
        {
            var version = Interlocked.Increment(ref acquisitionCount);
            return Task.FromResult(ValidToken($"token-{version}"));
        }

        var original = await cache.GetOrCreateAsync(
            "client-original",
            "secret-original",
            ["read"],
            Acquire,
            TestContext.Current.CancellationToken);
        var cachedOriginal = await cache.GetOrCreateAsync(
            "client-original",
            "secret-original",
            ["read"],
            Acquire,
            TestContext.Current.CancellationToken);
        var rotatedKey = await cache.GetOrCreateAsync(
            "client-rotated",
            "secret-original",
            ["read"],
            Acquire,
            TestContext.Current.CancellationToken);
        var rotatedSecret = await cache.GetOrCreateAsync(
            "client-rotated",
            "secret-rotated",
            ["read"],
            Acquire,
            TestContext.Current.CancellationToken);

        Assert.Equal(original, cachedOriginal);
        Assert.NotEqual(original, rotatedKey);
        Assert.NotEqual(rotatedKey, rotatedSecret);
        Assert.Equal(3, acquisitionCount);
    }

    [Fact]
    public async Task Unique_credential_cardinality_is_bounded()
    {
        var cache = new AccessTokenCache(TimeProvider.System);

        for (var index = 0; index < 256; index++)
        {
            await cache.GetOrCreateAsync(
                $"client-{index}",
                $"secret-{index}",
                ["read"],
                _ => Task.FromResult(ValidToken($"token-{index}")),
                TestContext.Current.CancellationToken);
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync(
                "client-over-limit",
                "secret-over-limit",
                ["read"],
                _ => Task.FromResult(ValidToken("never-acquired")),
                TestContext.Current.CancellationToken));

        Assert.Contains("bounded limit of 256", error.Message, StringComparison.Ordinal);
    }

    private static TokenResponse ValidToken(string value) => new()
    {
        AccessToken = value,
        ExpiresAt = DateTime.UtcNow.AddHours(1)
    };
}
