using Microsoft.Extensions.Options;

namespace Inspacy.Caching;

public sealed class InspacyHybridCacheOptions
{
    public const string SectionName = "Cache";

    public bool Enabled { get; set; } = true;

    public int DefaultExpirationSeconds { get; set; } = 60;

    public int DefaultLocalExpirationSeconds { get; set; } = 15;

    public int MaximumKeyLength { get; set; } = 256;

    public long MaximumPayloadBytes { get; set; } = 256 * 1024;

    public int SourceConcurrencyLimit { get; set; } = 64;

    public int SourceQueueTimeoutMilliseconds { get; set; } = 50;

    public InspacyRedisCacheOptions Redis { get; set; } = new();
}

public sealed class InspacyRedisCacheOptions
{
    public bool Enabled { get; set; }

    public string? ConnectionString { get; set; }

    public string InstanceName { get; set; } = "inspacy-cache:";

    public int ConnectTimeoutMilliseconds { get; set; } = 1000;

    public int OperationTimeoutMilliseconds { get; set; } = 250;
}

public sealed class InspacyHybridCacheOptionsValidator
    : IValidateOptions<InspacyHybridCacheOptions>
{
    private const int MinimumKeyLength = 192;
    private const int MaximumKeyLength = 1024;
    private const long MinimumPayloadBytes = 1024;
    private const long MaximumPayloadBytes = 1024 * 1024;

    public ValidateOptionsResult Validate(
        string? name,
        InspacyHybridCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.DefaultExpirationSeconds is < 1 or > 3600)
            failures.Add("Cache:DefaultExpirationSeconds must be between 1 and 3600.");

        if (options.DefaultLocalExpirationSeconds is < 1 or > 300)
            failures.Add("Cache:DefaultLocalExpirationSeconds must be between 1 and 300.");

        if (options.DefaultLocalExpirationSeconds > options.DefaultExpirationSeconds)
            failures.Add("Cache:DefaultLocalExpirationSeconds cannot exceed the L2 expiration.");

        if (options.MaximumKeyLength is < MinimumKeyLength or > MaximumKeyLength)
            failures.Add($"Cache:MaximumKeyLength must be between {MinimumKeyLength} and {MaximumKeyLength}.");

        if (options.MaximumPayloadBytes is < MinimumPayloadBytes or > MaximumPayloadBytes)
            failures.Add($"Cache:MaximumPayloadBytes must be between {MinimumPayloadBytes} and {MaximumPayloadBytes}.");

        if (options.SourceConcurrencyLimit is < 1 or > 1024)
            failures.Add("Cache:SourceConcurrencyLimit must be between 1 and 1024.");

        if (options.SourceQueueTimeoutMilliseconds is < 1 or > 5000)
            failures.Add("Cache:SourceQueueTimeoutMilliseconds must be between 1 and 5000.");

        if (options.Redis is null)
        {
            failures.Add("Cache:Redis is required.");
        }
        else
        {
            if (options.Redis.Enabled && string.IsNullOrWhiteSpace(options.Redis.ConnectionString))
                failures.Add("Cache:Redis:ConnectionString is required when Redis is enabled.");

            if (options.Redis.ConnectionString?.Length > 4096)
                failures.Add("Cache:Redis:ConnectionString cannot exceed 4096 characters.");

            if (string.IsNullOrWhiteSpace(options.Redis.InstanceName)
                || options.Redis.InstanceName.Length > 64)
            {
                failures.Add("Cache:Redis:InstanceName must contain between 1 and 64 characters.");
            }
            else if (options.Redis.InstanceName.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ':' and not '_' and not '.' and not '-'))
            {
                failures.Add("Cache:Redis:InstanceName can contain only ASCII letters, digits, colons, dots, hyphens, or underscores.");
            }

            if (options.Redis.ConnectTimeoutMilliseconds is < 100 or > 5000)
                failures.Add("Cache:Redis:ConnectTimeoutMilliseconds must be between 100 and 5000.");

            if (options.Redis.OperationTimeoutMilliseconds is < 25 or > 5000)
                failures.Add("Cache:Redis:OperationTimeoutMilliseconds must be between 25 and 5000.");

            if (options.Redis.Enabled
                && !string.IsNullOrWhiteSpace(options.Redis.ConnectionString))
            {
                try
                {
                    _ = StackExchange.Redis.ConfigurationOptions.Parse(
                        options.Redis.ConnectionString,
                        ignoreUnknown: false);
                }
                catch (Exception)
                {
                    failures.Add("Cache:Redis:ConnectionString is not a valid StackExchange.Redis configuration.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
