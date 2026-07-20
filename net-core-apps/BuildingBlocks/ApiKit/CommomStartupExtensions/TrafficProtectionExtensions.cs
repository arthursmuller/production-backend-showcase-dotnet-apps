using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommomStartupExtensions;

public sealed class TrafficProtectionOptions
{
    public const string SectionName = "Traffic";

    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxConcurrentRequestsPerReplica { get; set; } = 256;
    public long MaxRequestBodyBytes { get; set; } = 2 * 1024 * 1024;
    public long MaxMultipartBodyBytes { get; set; } = 32 * 1024 * 1024;
    public int ForwardLimit { get; set; } = 2;
    public string[] KnownProxies { get; set; } = [];
    public int AuthenticatedWritePermitLimitPerMinute { get; set; } = 60;
    public int AuthenticatedWritePartitionCount { get; set; } = 4_096;
    public int AnonymousViewPermitLimitPerMinute { get; set; } = 600;
    public int AnonymousViewPartitionCount { get; set; } = 4_096;

    internal void Validate()
    {
        if (RequestTimeoutSeconds is < 1 or > 300)
            throw new InvalidOperationException("Traffic:RequestTimeoutSeconds must be between 1 and 300.");
        if (MaxConcurrentRequestsPerReplica is < 1 or > 100_000)
            throw new InvalidOperationException(
                "Traffic:MaxConcurrentRequestsPerReplica must be between 1 and 100000.");
        if (MaxRequestBodyBytes is < 1 or > 64 * 1024 * 1024)
            throw new InvalidOperationException("Traffic:MaxRequestBodyBytes is outside the allowed range.");
        if (MaxMultipartBodyBytes is < 1 or > 512 * 1024 * 1024)
            throw new InvalidOperationException("Traffic:MaxMultipartBodyBytes is outside the allowed range.");
        if (ForwardLimit is < 1 or > 5)
            throw new InvalidOperationException("Traffic:ForwardLimit must be between 1 and 5.");
        if (KnownProxies.Length > 32)
            throw new InvalidOperationException("Traffic:KnownProxies accepts at most 32 addresses.");
        if (AuthenticatedWritePermitLimitPerMinute is < 1 or > 10_000)
            throw new InvalidOperationException(
                "Traffic:AuthenticatedWritePermitLimitPerMinute must be between 1 and 10000.");
        if (AuthenticatedWritePartitionCount is < 64 or > 16_384)
            throw new InvalidOperationException(
                "Traffic:AuthenticatedWritePartitionCount must be between 64 and 16384.");
        if (AnonymousViewPermitLimitPerMinute is < 1 or > 100_000)
            throw new InvalidOperationException(
                "Traffic:AnonymousViewPermitLimitPerMinute must be between 1 and 100000.");
        if (AnonymousViewPartitionCount is < 64 or > 16_384)
            throw new InvalidOperationException(
                "Traffic:AnonymousViewPartitionCount must be between 64 and 16384.");
    }
}

public static class TrafficProtectionPolicies
{
    public const string AuthenticatedWrite = "authenticated-write";
    public const string AnonymousView = "anonymous-view";
}

public static class TrafficProtectionExtensions
{
    private const string CapacityLoadShedReason = "capacity_load_shed";
    private const string ClientQuotaReason = "client_quota";

    public static TrafficProtectionOptions ConfigureTrafficProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection(TrafficProtectionOptions.SectionName)
            .Get<TrafficProtectionOptions>() ?? new TrafficProtectionOptions();
        settings.Validate();

        services.AddSingleton(settings);
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            options.MultipartBodyLengthLimit = settings.MaxMultipartBodyBytes);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = settings.ForwardLimit;

            // ForwardedHeadersOptions trusts loopback networks by default. Production trust
            // must be an explicit deployment decision: a local sidecar, SSRF target, or
            // accidentally exposed development proxy must not be able to rewrite the request
            // origin merely because it connected over loopback.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            // The middleware treats two empty collections as "trust every proxy". Keep a
            // non-routable sentinel so an empty configured allow-list means trust none.
            options.KnownProxies.Add(IPAddress.None);
            foreach (var configuredAddress in settings.KnownProxies)
            {
                if (!IPAddress.TryParse(configuredAddress, out var address))
                    throw new InvalidOperationException(
                        $"Traffic:KnownProxies contains invalid IP address '{configuredAddress}'.");
                options.KnownProxies.Add(address);
            }
        });

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            };
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.Get(
                    partitionKey: "replica",
                    _ => new OutcomeTaggedRateLimiter(
                        new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                        {
                            PermitLimit = settings.MaxConcurrentRequestsPerReplica,
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        }),
                        CapacityLoadShedReason)));
            options.AddPolicy<string>(
                TrafficProtectionPolicies.AuthenticatedWrite,
                context =>
                {
                    var subject = context.User.FindFirst("sub")?.Value
                        ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? "anonymous";
                    var hash = unchecked((uint)StringComparer.Ordinal.GetHashCode(subject));
                    var shard = hash % (uint)settings.AuthenticatedWritePartitionCount;
                    return RateLimitPartition.Get(
                        $"write-{shard}",
                        _ => new OutcomeTaggedRateLimiter(
                            new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                            {
                                TokenLimit = settings.AuthenticatedWritePermitLimitPerMinute,
                                TokensPerPeriod = settings.AuthenticatedWritePermitLimitPerMinute,
                                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                                AutoReplenishment = true,
                                QueueLimit = 0,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            }),
                            ClientQuotaReason));
                });
            options.AddPolicy<string>(
                TrafficProtectionPolicies.AnonymousView,
                context =>
                {
                    // Forwarded headers are resolved against the configured trusted-proxy
                    // allow-list before rate limiting. Hashing into a fixed shard inventory
                    // prevents attacker-controlled source addresses from allocating limiters.
                    var source = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var hash = unchecked((uint)StringComparer.Ordinal.GetHashCode(source));
                    var shard = hash % (uint)settings.AnonymousViewPartitionCount;
                    return RateLimitPartition.Get(
                        $"view-{shard}",
                        _ => new OutcomeTaggedRateLimiter(
                            new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                            {
                                TokenLimit = settings.AnonymousViewPermitLimitPerMinute,
                                TokensPerPeriod = settings.AnonymousViewPermitLimitPerMinute,
                                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                                AutoReplenishment = true,
                                QueueLimit = 0,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            }),
                            ClientQuotaReason));
                });
            options.OnRejected = async (context, cancellationToken) =>
            {
                var isCapacityLoadShed = context.Lease.TryGetMetadata(
                        MetadataName.ReasonPhrase,
                        out var rejectionReason)
                    && string.Equals(
                        rejectionReason,
                        CapacityLoadShedReason,
                        StringComparison.Ordinal);
                var status = isCapacityLoadShed
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status429TooManyRequests;
                var code = isCapacityLoadShed ? "capacity_load_shed" : "rate_limited";
                context.HttpContext.Items[HttpRequestOutcomeContext.ItemKey] = isCapacityLoadShed
                    ? HttpRequestOutcome.LoadShed
                    : HttpRequestOutcome.ClientRateLimited;
                context.HttpContext.Response.StatusCode = status;
                context.HttpContext.Response.Headers.RetryAfter = "1";
                var problemDetails = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();
                await problemDetails.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = status,
                        Title = isCapacityLoadShed
                            ? "Service temporarily overloaded"
                            : "Request rate limit exceeded",
                        Detail = isCapacityLoadShed
                            ? "The service is at its configured concurrency limit. Retry with backoff."
                            : "The caller exceeded the configured request quota. Retry later.",
                        Instance = context.HttpContext.Request.Path,
                        Type = $"https://httpstatuses.io/{status}",
                        Extensions = { ["code"] = code },
                    },
                });
            };
        });

        return settings;
    }
}
