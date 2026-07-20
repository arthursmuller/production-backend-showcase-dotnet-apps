using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Inspacy.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CommomStartupExtensions;

public static class PlatformTelemetry
{
    public const string ActivitySourceName = PlatformInstrumentation.ActivitySourceName;
    public const string MeterName = PlatformInstrumentation.MeterName;

    public static readonly ActivitySource ActivitySource = PlatformInstrumentation.ActivitySource;
    public static readonly Meter Meter = PlatformInstrumentation.Meter;
    public static readonly Counter<long> RequestOutcomes = Meter.CreateCounter<long>(
        "inspacy.http.server.request.outcomes",
        unit: "{request}",
        description: "Completed request outcomes classified into a fixed eligibility set.");
}

public static class ObservabilityExtensions
{
    public static IServiceCollection ConfigureObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var environment = configuration["DOTNET_ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? "Production";
        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: version)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment.name"] = environment,
            });

        var openTelemetry = services.AddOpenTelemetry().ConfigureResource(builder =>
            builder.AddService(serviceName, serviceVersion: version)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = environment,
                }));

        var tracing = openTelemetry.WithTracing(builder => builder
            .AddSource(PlatformTelemetry.ActivitySourceName)
            .AddSource("Inspacy.Caching")
            .AddSource("MassTransit")
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = false;
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health/live");
            })
            .AddHttpClientInstrumentation(options => options.RecordException = false));

        var metrics = openTelemetry.WithMetrics(builder => builder
            .AddMeter(PlatformTelemetry.MeterName)
            .AddMeter("Inspacy.Caching")
            .AddMeter("MassTransit")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation());

        var endpointValue = configuration["OpenTelemetry:OtlpEndpoint"]
            ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(endpointValue))
        {
            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
                || (!string.Equals(endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "OpenTelemetry:OtlpEndpoint must be an absolute HTTP(S) URI.");
            }

            tracing.WithTracing(builder => builder.AddOtlpExporter(options =>
                options.Endpoint = endpoint));
            metrics.WithMetrics(builder => builder.AddOtlpExporter(options =>
                options.Endpoint = endpoint));
        }

        services.AddSingleton(resource);
        return services;
    }
}

public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    private const string CorrelationHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var correlationId = GetCorrelationId(context);
        context.Response.Headers[CorrelationHeader] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            ["SpanId"] = Activity.Current?.SpanId.ToString(),
            ["CorrelationId"] = correlationId,
            ["RequestId"] = context.TraceIdentifier,
        });

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Items[HttpRequestOutcomeContext.ItemKey] = HttpRequestOutcome.ClientCancelled;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                ?? "unmatched";
            var outcome = ResolveOutcome(context);
            PlatformTelemetry.RequestOutcomes.Add(
                1,
                new KeyValuePair<string, object?>(
                    "http_request_outcome",
                    ToMetricLabel(outcome)),
                new KeyValuePair<string, object?>("http_route", route));
            logger.LogInformation(
                "HTTP {Method} {Route} completed with {StatusCode} in {ElapsedMilliseconds:F2} ms; Outcome={Outcome}; Cancelled={Cancelled}.",
                context.Request.Method,
                route,
                context.Response.StatusCode,
                elapsed.TotalMilliseconds,
                ToMetricLabel(outcome),
                outcome == HttpRequestOutcome.ClientCancelled);
        }
    }

    internal static HttpRequestOutcome ResolveOutcome(HttpContext context)
    {
        if (context.Items.TryGetValue(HttpRequestOutcomeContext.ItemKey, out var explicitValue)
            && explicitValue is HttpRequestOutcome explicitOutcome)
        {
            return explicitOutcome;
        }

        var statusCode = context.Response.StatusCode;
        if (statusCode == StatusCodes.Status504GatewayTimeout)
            return HttpRequestOutcome.ServerTimeout;
        if (context.RequestAborted.IsCancellationRequested)
            return HttpRequestOutcome.ClientCancelled;
        if (statusCode < StatusCodes.Status400BadRequest)
            return HttpRequestOutcome.Success;

        return statusCode switch
        {
            StatusCodes.Status400BadRequest or
            StatusCodes.Status405MethodNotAllowed or
            StatusCodes.Status406NotAcceptable or
            StatusCodes.Status411LengthRequired or
            StatusCodes.Status413PayloadTooLarge or
            StatusCodes.Status414UriTooLong or
            StatusCodes.Status415UnsupportedMediaType or
            StatusCodes.Status422UnprocessableEntity => HttpRequestOutcome.InvalidRequest,
            StatusCodes.Status401Unauthorized => HttpRequestOutcome.AuthenticationRequired,
            StatusCodes.Status403Forbidden => HttpRequestOutcome.AccessForbidden,
            StatusCodes.Status404NotFound => HttpRequestOutcome.NotFound,
            StatusCodes.Status409Conflict => HttpRequestOutcome.Conflict,
            >= StatusCodes.Status500InternalServerError => HttpRequestOutcome.ServerError,
            _ => HttpRequestOutcome.BusinessRejection,
        };
    }

    internal static string ToMetricLabel(HttpRequestOutcome outcome) => outcome switch
    {
        HttpRequestOutcome.Success => "success",
        HttpRequestOutcome.InvalidRequest => "invalid_request",
        HttpRequestOutcome.AuthenticationRequired => "authentication_required",
        HttpRequestOutcome.AccessForbidden => "access_forbidden",
        HttpRequestOutcome.NotFound => "not_found",
        HttpRequestOutcome.Conflict => "conflict",
        HttpRequestOutcome.BusinessRejection => "business_rejection",
        HttpRequestOutcome.ClientRateLimited => "client_rate_limited",
        HttpRequestOutcome.LoadShed => "load_shed",
        HttpRequestOutcome.ServerTimeout => "server_timeout",
        HttpRequestOutcome.DependencyUnavailable => "dependency_unavailable",
        HttpRequestOutcome.ServerError => "server_error",
        HttpRequestOutcome.ClientCancelled => "client_cancelled",
        _ => "server_error",
    };

    private static string GetCorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(supplied)
            && supplied.Length <= 128
            && supplied.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':'))
        {
            return supplied;
        }

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
