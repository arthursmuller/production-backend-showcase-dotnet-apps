using System.Diagnostics;
using DomainExceptions;
using Inspacy.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApiExceptionHandling;

public static class ApiProblemDetailsExtensions
{
    public static IServiceCollection AddInspacyProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var traceId = Activity.Current?.TraceId.ToString()
                    ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["traceId"] = traceId;
                context.ProblemDetails.Extensions["correlationId"] =
                    CorrelationId.Get(context.HttpContext, traceId);
            };
        });
        services.AddExceptionHandler<ClientCancellationExceptionHandler>();
        services.AddExceptionHandler<BadRequestExceptionHandler>();
        services.AddExceptionHandler<CacheSourceOverloadExceptionHandler>();
        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddExceptionHandler<UnexpectedExceptionHandler>();
        return services;
    }

    /// <summary>
    /// Produces a stable, non-sensitive RFC 7807 response when authentication or
    /// authorization short-circuits before an endpoint can write a response body.
    /// Protocol handlers remain responsible for challenge headers such as
    /// <c>WWW-Authenticate</c>.
    /// </summary>
    public static IApplicationBuilder UseInspacySecurityStatusCodePages(
        this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.UseStatusCodePages(async statusContext =>
        {
            var httpContext = statusContext.HttpContext;
            var identityUnavailable = httpContext.Response.StatusCode ==
                    StatusCodes.Status500InternalServerError
                && httpContext.Response.Headers.WWWAuthenticate.Any(value =>
                    value?.Contains("error=\"server_error\"", StringComparison.OrdinalIgnoreCase) == true);
            if (identityUnavailable)
            {
                // OpenIddict uses a server_error challenge when discovery, JWKS, or
                // introspection cannot establish identity state. This is a dependency
                // outage, not an application exception and never a reason to accept the
                // caller. Surface a retryable, body-safe 503 instead of an empty 500.
                httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                httpContext.Response.Headers.RetryAfter = "1";
            }

            var (title, detail, code) = identityUnavailable
                ? (
                    "Identity validation unavailable",
                    "The caller's identity could not be verified. Retry with backoff.",
                    "identity_validation_unavailable")
                : httpContext.Response.StatusCode switch
                {
                    StatusCodes.Status401Unauthorized => (
                        "Authentication required",
                        "A valid access token is required to access this resource.",
                        "authentication_required"),
                    StatusCodes.Status403Forbidden => (
                        "Access forbidden",
                        "The authenticated identity is not permitted to access this resource.",
                        "access_forbidden"),
                    StatusCodes.Status504GatewayTimeout => (
                        "Request deadline exceeded",
                        "The operation did not complete before its configured deadline.",
                        "request_timeout"),
                    _ => (null, null, null),
                };

            if (code is null)
                return;

            httpContext.Items[HttpRequestOutcomeContext.ItemKey] = code switch
            {
                "authentication_required" => HttpRequestOutcome.AuthenticationRequired,
                "access_forbidden" => HttpRequestOutcome.AccessForbidden,
                "request_timeout" => HttpRequestOutcome.ServerTimeout,
                "identity_validation_unavailable" => HttpRequestOutcome.DependencyUnavailable,
                _ => HttpRequestOutcome.ServerError,
            };
            httpContext.Response.Headers.CacheControl = "no-store";
            var problemDetailsService = httpContext.RequestServices
                .GetRequiredService<IProblemDetailsService>();
            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = httpContext.Response.StatusCode,
                    Title = title,
                    Detail = detail,
                    Instance = httpContext.Request.Path,
                    Type = $"https://httpstatuses.io/{httpContext.Response.StatusCode}",
                    Extensions = { ["code"] = code },
                },
            });
        });
    }
}

internal sealed class CacheSourceOverloadExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<CacheSourceOverloadExceptionHandler> logger)
    : ApiExceptionHandlerBase(problemDetailsService)
{
    public override ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not CacheSourceOverloadedException)
            return ValueTask.FromResult(false);

        logger.LogWarning(
            "Authoritative cache-source concurrency budget rejected {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);
        httpContext.Items[HttpRequestOutcomeContext.ItemKey] = HttpRequestOutcome.LoadShed;
        httpContext.Response.Headers.RetryAfter = "1";
        return WriteAsync(
            httpContext,
            StatusCodes.Status503ServiceUnavailable,
            "Service temporarily overloaded",
            "The request could not enter the bounded dependency concurrency budget. Retry with backoff.",
            "cache_source_overloaded",
            cancellationToken);
    }
}

internal static class CorrelationId
{
    private const string HeaderName = "X-Correlation-ID";
    private const int MaxLength = 128;

    public static string Get(HttpContext context, string fallback)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        if (IsValid(supplied))
            return supplied!;
        return fallback;
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxLength
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':');
}

internal abstract class ApiExceptionHandlerBase(IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public abstract ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken);

    protected async ValueTask<bool> WriteAsync(
        HttpContext context,
        int status,
        string title,
        string detail,
        string code,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["code"] = code;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        });
    }
}

internal sealed class ClientCancellationExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ClientCancellationExceptionHandler> logger)
    : ApiExceptionHandlerBase(problemDetailsService)
{
    public override async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not OperationCanceledException)
            return false;

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Items[HttpRequestOutcomeContext.ItemKey] =
                HttpRequestOutcome.ClientCancelled;
            logger.LogInformation(
                "Request was cancelled by the client for {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);
            return true;
        }

        logger.LogWarning(
            "Request deadline elapsed for {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);
        httpContext.Items[HttpRequestOutcomeContext.ItemKey] = HttpRequestOutcome.ServerTimeout;
        return await WriteAsync(
            httpContext,
            StatusCodes.Status504GatewayTimeout,
            "Request deadline exceeded",
            "The operation did not complete before its configured deadline.",
            "request_timeout",
            cancellationToken);
    }
}

internal sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetailsService)
    : ApiExceptionHandlerBase(problemDetailsService)
{
    public override ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
            return ValueTask.FromResult(false);

        httpContext.Items[HttpRequestOutcomeContext.ItemKey] = HttpRequestOutcome.InvalidRequest;
        return WriteAsync(
            httpContext,
            badRequest.StatusCode,
            "Invalid request",
            badRequest.Message,
            "invalid_request",
            cancellationToken);
    }
}

internal sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger)
    : ApiExceptionHandlerBase(problemDetailsService)
{
    public override ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return ValueTask.FromResult(false);

        logger.LogInformation(
            "Domain request rejected with {StatusCode} for {Method} {Path}.",
            domainException.StatusCode,
            httpContext.Request.Method,
            httpContext.Request.Path);
        httpContext.Items[HttpRequestOutcomeContext.ItemKey] = domainException.StatusCode switch
        {
            StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity =>
                HttpRequestOutcome.InvalidRequest,
            StatusCodes.Status401Unauthorized => HttpRequestOutcome.AuthenticationRequired,
            StatusCodes.Status403Forbidden => HttpRequestOutcome.AccessForbidden,
            StatusCodes.Status404NotFound => HttpRequestOutcome.NotFound,
            StatusCodes.Status409Conflict => HttpRequestOutcome.Conflict,
            _ => HttpRequestOutcome.BusinessRejection,
        };
        return WriteAsync(
            httpContext,
            domainException.StatusCode,
            "Domain rule violation",
            domainException.Message,
            "domain_rule_violation",
            cancellationToken);
    }
}

internal sealed class UnexpectedExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UnexpectedExceptionHandler> logger)
    : ApiExceptionHandlerBase(problemDetailsService)
{
    public override ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        httpContext.Items[HttpRequestOutcomeContext.ItemKey] = HttpRequestOutcome.ServerError;
        logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);
        return WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "Unexpected error",
            "An unexpected error occurred while processing the request.",
            "unexpected_error",
            cancellationToken);
    }
}
