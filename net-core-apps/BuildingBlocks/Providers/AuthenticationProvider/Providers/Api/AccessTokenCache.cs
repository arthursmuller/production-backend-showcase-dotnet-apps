#nullable enable

using AuthenticationProvider.Dtos;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AuthenticationProvider.Providers.Api;

/// <summary>
/// A bounded, process-local cache that coalesces token acquisition per unique
/// client credential and scope set. It deliberately never exposes cache keys,
/// which are SHA-256 digests containing no plaintext client secret.
/// </summary>
public sealed class AccessTokenCache
{
    private const int MaximumEntries = 256;
    private readonly ConcurrentDictionary<string, CacheSlot> _entries = new(StringComparer.Ordinal);
    private readonly object _entryCreationLock = new();
    private readonly TimeProvider _timeProvider;

    public AccessTokenCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<string> GetOrCreateAsync(
        string clientId,
        string clientSecret,
        string[] scopes,
        Func<CancellationToken, Task<TokenResponse>> acquire,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(acquire);

        var key = CreateCacheKey(clientId, clientSecret, scopes);
        var slot = GetOrAddSlot(key);

        var now = _timeProvider.GetUtcNow();
        var current = slot.Value;
        if (current is not null && current.RefreshAtUtc > now)
            return current.AccessToken;

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            current = slot.Value;
            if (current is not null && current.RefreshAtUtc > now)
                return current.AccessToken;

            var token = await acquire(cancellationToken).ConfigureAwait(false);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("The identity service returned an empty access token.");
            if (token.ExpiresAt <= now.UtcDateTime)
                throw new InvalidOperationException("The identity service returned an already expired access token.");

            var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(token.ExpiresAt, DateTimeKind.Utc));
            var lifetime = expiresAt - now;
            var refreshSkewSeconds = Math.Clamp(lifetime.TotalSeconds * 0.10, 5, 300);
            var refreshAt = expiresAt - TimeSpan.FromSeconds(refreshSkewSeconds);

            slot.Value = new CachedToken(token.AccessToken, refreshAt);
            return token.AccessToken;
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private CacheSlot GetOrAddSlot(string key)
    {
        if (_entries.TryGetValue(key, out var existing))
            return existing;

        lock (_entryCreationLock)
        {
            if (_entries.TryGetValue(key, out existing))
                return existing;

            RemoveExpiredEntries();
            if (_entries.Count >= MaximumEntries)
            {
                throw new InvalidOperationException(
                    $"The access-token cache reached its bounded limit of {MaximumEntries} unique credential/scope sets.");
            }

            var created = new CacheSlot();
            if (!_entries.TryAdd(key, created))
                return _entries[key];

            return created;
        }
    }

    private void RemoveExpiredEntries()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _entries)
        {
            if (pair.Value.Value is { } value && value.RefreshAtUtc <= now)
                _entries.TryRemove(pair.Key, out _);
        }
    }

    private static string CreateCacheKey(string clientId, string clientSecret, string[] scopes)
    {
        var normalizedScopes = scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(scope => scope, StringComparer.Ordinal);

        var material = string.Join('\u001f', new[] { clientId, clientSecret }.Concat(normalizedScopes));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private sealed class CacheSlot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public volatile CachedToken? Value;
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAtUtc);
}
