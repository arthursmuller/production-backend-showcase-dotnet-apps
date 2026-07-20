using System.Security.Cryptography;
using System.Text;

namespace Inspacy.Caching;

public readonly record struct CacheKey
{
    internal CacheKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public interface ICacheKeyFactory
{
    CacheKey Create(
        string resource,
        string entityId,
        long entityVersion,
        string? tenantId = null);
}

public sealed class CacheKeyFactory : ICacheKeyFactory
{
    private const int MaximumNamespaceSegmentLength = 64;
    private const int MaximumIdentifierLength = 512;
    private const int MaximumTenantIdentifierLength = 256;
    private readonly string _environment;
    private readonly string _service;
    private readonly int _maximumKeyLength;

    public CacheKeyFactory(
        CacheNamespace cacheNamespace,
        Microsoft.Extensions.Options.IOptions<InspacyHybridCacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cacheNamespace);
        ArgumentNullException.ThrowIfNull(options);

        _environment = cacheNamespace.EnvironmentName;
        _service = cacheNamespace.ServiceName;
        _maximumKeyLength = options.Value.MaximumKeyLength;
    }

    public CacheKey Create(
        string resource,
        string entityId,
        long entityVersion,
        string? tenantId = null)
    {
        var normalizedResource = NormalizeNamespaceSegment(resource, nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (entityId.Length > MaximumIdentifierLength)
            throw new ArgumentOutOfRangeException(nameof(entityId), $"Entity identifiers cannot exceed {MaximumIdentifierLength} characters.");

        if (tenantId?.Length > MaximumTenantIdentifierLength)
            throw new ArgumentOutOfRangeException(nameof(tenantId), $"Tenant identifiers cannot exceed {MaximumTenantIdentifierLength} characters.");

        if (entityVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(entityVersion), "Entity version cannot be negative.");

        var tenant = string.IsNullOrWhiteSpace(tenantId)
            ? "global"
            : $"tenant-{Hash(tenantId)}";
        var key = $"inspacy:{_environment}:{_service}:{tenant}:{normalizedResource}:id-{Hash(entityId)}:e{entityVersion}";

        if (key.Length > _maximumKeyLength)
        {
            throw new InvalidOperationException(
                $"The generated cache key is {key.Length} characters, exceeding the configured {_maximumKeyLength}-character limit.");
        }

        return new CacheKey(key);
    }

    private static string NormalizeNamespaceSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaximumNamespaceSegmentLength
            || !normalized.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.'))
        {
            throw new ArgumentException(
                "Cache namespace segments must be 1-64 ASCII letters, digits, dots, hyphens, or underscores.",
                parameterName);
        }

        return normalized;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }
}

public sealed class CacheNamespace
{
    public CacheNamespace(string environmentName, string serviceName)
    {
        EnvironmentName = Normalize(environmentName, nameof(environmentName));
        ServiceName = Normalize(serviceName, nameof(serviceName));
    }

    public string EnvironmentName { get; }

    public string ServiceName { get; }

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > 64
            || !normalized.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.'))
        {
            throw new ArgumentException(
                "Cache namespace segments must be 1-64 ASCII letters, digits, dots, hyphens, or underscores.",
                parameterName);
        }

        return normalized;
    }
}
