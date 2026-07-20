using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CommomStartupExtensions;
using InspacyApiHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Validation.SystemNetHttp;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class IdentityEdgeAcceptanceTests
{
    [Fact]
    public async Task Invalid_and_insufficient_identities_fail_closed_with_canonical_safe_responses()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(identity);

        var invalidTokens = new string?[]
        {
            null,
            "not-a-token",
            "expired",
            "revoked",
            "wrong-audience",
        };

        foreach (var token in invalidTokens)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "protected");
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await api.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"Token '{token ?? "<missing>"}' returned {response.StatusCode}.{Environment.NewLine}" +
                string.Join(Environment.NewLine, api.Logs.Messages));
            await AssertCanonicalProblemAsync(
                response,
                HttpStatusCode.Unauthorized,
                "authentication_required",
                token);
            Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        using var forbiddenRequest = AuthorizedRequest(HttpMethod.Get, "admin", "valid:ordinary-user");
        using var forbidden = await api.Client.SendAsync(
            forbiddenRequest,
            TestContext.Current.CancellationToken);
        await AssertCanonicalProblemAsync(
            forbidden,
            HttpStatusCode.Forbidden,
            "access_forbidden",
            "valid:ordinary-user");

        using var validRequest = AuthorizedRequest(HttpMethod.Get, "protected", "valid:ordinary-user");
        using var valid = await api.Client.SendAsync(
            validRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);

        Assert.InRange(identity.DiscoveryRequests, 1, 2);
        Assert.DoesNotContain(
            api.Logs.Messages,
            message => message.Contains("valid:ordinary-user", StringComparison.Ordinal)
                || message.Contains(IdentityStub.ClientSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_proxy_and_identity_headers_cannot_rewrite_request_or_user_context()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(identity);
        using var request = new HttpRequestMessage(HttpMethod.Get, "context");
        request.Headers.TryAddWithoutValidation(
            "Forwarded",
            "for=203.0.113.9;proto=https;host=forwarded.attacker.test");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.17");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "attacker.test");
        request.Headers.TryAddWithoutValidation("X-User-Id", "administrator");
        request.Headers.TryAddWithoutValidation("X-Client-Id", "trusted-client");

        using var response = await api.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<ContextSnapshot>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal("http", snapshot.Scheme);
        Assert.Equal(api.BaseAddress.Authority, snapshot.Host);
        Assert.Equal(IPAddress.Loopback.ToString(), snapshot.RemoteIp);
        Assert.False(snapshot.Authenticated);
        Assert.Null(snapshot.Subject);
    }

    [Fact]
    public async Task Allow_listed_proxy_chain_honors_forward_limit_without_trusting_leftmost_spoof()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(
            identity,
            knownProxies: ["127.0.0.1", "10.0.0.2", "10.0.0.3"],
            forwardLimit: 2);
        using var request = new HttpRequestMessage(HttpMethod.Get, "context");
        request.Headers.TryAddWithoutValidation(
            "Forwarded",
            "for=192.0.2.99;proto=http;host=ignored.attacker.test");
        request.Headers.TryAddWithoutValidation(
            "X-Forwarded-For",
            "198.51.100.44, 10.0.0.3, 10.0.0.2");
        request.Headers.TryAddWithoutValidation(
            "X-Forwarded-Proto",
            "http, https, http");
        request.Headers.TryAddWithoutValidation(
            "X-Forwarded-Host",
            "spoof.attacker.test, inner.proxy.test, edge.proxy.test");

        using var response = await api.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<ContextSnapshot>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal("https", snapshot.Scheme);
        Assert.Equal("inner.proxy.test", snapshot.Host);
        Assert.Equal("10.0.0.3", snapshot.RemoteIp);
        Assert.False(snapshot.Authenticated);
    }

    [Fact]
    public async Task Identity_5xx_timeout_retry_breaker_and_discovery_cache_never_accept_unknown_state()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(identity, fastIdentityFailures: true);

        // Establish a successful discovery/introspection path before injecting the slow
        // token. Under parallel coverage load, Windows can spend this test's deliberately
        // short first attempt budget initializing TLS and fail closed before the request
        // reaches the introspection endpoint, which does not exercise the behavior below.
        await WarmIdentityTransportAsync(api);

        using (var timeoutRequest = AuthorizedRequest(HttpMethod.Get, "protected", "timeout"))
        using (var timeoutResponse = await api.Client.SendAsync(
            timeoutRequest,
            TestContext.Current.CancellationToken))
        {
            await AssertCanonicalProblemAsync(
                timeoutResponse,
                HttpStatusCode.ServiceUnavailable,
                "identity_validation_unavailable",
                "timeout");
            Assert.Equal("1", timeoutResponse.Headers.RetryAfter?.ToString());
        }

        Assert.InRange(identity.TimeoutIntrospectionRequests, 1, 3);

        var beforeOutage = identity.OutageIntrospectionRequests;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var request = AuthorizedRequest(HttpMethod.Get, "protected", "outage");
            using var response = await api.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            await AssertCanonicalProblemAsync(
                response,
                HttpStatusCode.ServiceUnavailable,
                "identity_validation_unavailable",
                "outage");
            Assert.Equal("1", response.Headers.RetryAfter?.ToString());
        }

        var outageAttempts = identity.OutageIntrospectionRequests - beforeOutage;
        Assert.InRange(outageAttempts, 1, 30);
        Assert.True(outageAttempts < 30, "The identity circuit breaker never opened.");
        Assert.InRange(identity.DiscoveryRequests, 1, 2);
        Assert.DoesNotContain(
            api.Logs.Messages,
            message => message.Contains(IdentityStub.ClientSecret, StringComparison.Ordinal)
                || message.Contains("Bearer outage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discovery_tokens_enforce_claims_and_signing_key_rotation_with_bounded_overlap()
    {
        await using var identity = await IdentityStub.StartAsync();
        var oldToken = identity.CreateToken(SigningKeyVersion.Old, subject: "rotation-user");
        string newToken;
        await using (var initialApi = await ApiHarness.StartAsync(
            identity,
            validationMode: "Discovery"))
        {
            await AssertAuthorizedAsync(initialApi, oldToken);

            var locallyInvalidTokens = new[]
            {
                identity.CreateToken(
                    SigningKeyVersion.Old,
                    subject: "wrong-audience",
                    audience: "different-api"),
                identity.CreateToken(
                    SigningKeyVersion.Old,
                    subject: "wrong-issuer",
                    issuer: "https://untrusted-issuer.test/"),
                identity.CreateToken(
                    SigningKeyVersion.Old,
                    subject: "expired",
                    expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)),
            };
            foreach (var token in locallyInvalidTokens)
            {
                using var request = AuthorizedRequest(HttpMethod.Get, "protected", token);
                using var response = await initialApi.Client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                await AssertCanonicalProblemAsync(
                    response,
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    token);
            }

            Assert.InRange(identity.JsonWebKeySetRequests, 1, 2);
            identity.BeginSigningKeyOverlap();
            newToken = identity.CreateToken(SigningKeyVersion.New, subject: "rotation-user");

            // A running process keeps its last-known-good JWKS. Deployments therefore
            // publish an overlap set before the bounded rolling restart/metadata refresh.
            using var preRefreshRequest = AuthorizedRequest(HttpMethod.Get, "protected", newToken);
            using var preRefreshResponse = await initialApi.Client.SendAsync(
                preRefreshRequest,
                TestContext.Current.CancellationToken);
            await AssertCanonicalProblemAsync(
                preRefreshResponse,
                HttpStatusCode.Unauthorized,
                "authentication_required",
                newToken);
        }

        var beforeOverlapRefresh = identity.JsonWebKeySetRequests;
        await using (var overlapApi = await ApiHarness.StartAsync(
            identity,
            validationMode: "Discovery"))
        {
            await AssertAuthorizedAsync(overlapApi, oldToken);
            await AssertAuthorizedAsync(overlapApi, newToken);
            Assert.True(identity.JsonWebKeySetRequests > beforeOverlapRefresh);

            identity.CompleteSigningKeyRotation();
            // The old key remains valid in an already-refreshed overlap cache until the
            // process performs its next bounded refresh/restart.
            await AssertAuthorizedAsync(overlapApi, oldToken);
        }

        await using var cutoverApi = await ApiHarness.StartAsync(
            identity,
            validationMode: "Discovery");
        using (var retiredRequest = AuthorizedRequest(HttpMethod.Get, "protected", oldToken))
        using (var retiredResponse = await cutoverApi.Client.SendAsync(
            retiredRequest,
            TestContext.Current.CancellationToken))
        {
            await AssertCanonicalProblemAsync(
                retiredResponse,
                HttpStatusCode.Unauthorized,
                "authentication_required",
                oldToken);
        }
        await AssertAuthorizedAsync(cutoverApi, newToken);
    }

    [Fact]
    public async Task Authenticated_write_limiter_returns_bounded_retry_after_and_bounds_actor_cardinality()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(
            identity,
            writePermitLimit: 1,
            writePartitions: 64);

        using (var firstRequest = AuthorizedRequest(HttpMethod.Post, "write", "valid:repeat-user"))
        using (var first = await api.Client.SendAsync(
            firstRequest,
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using (var secondRequest = AuthorizedRequest(HttpMethod.Post, "write", "valid:repeat-user"))
        using (var second = await api.Client.SendAsync(
            secondRequest,
            TestContext.Current.CancellationToken))
        {
            await AssertCanonicalProblemAsync(
                second,
                HttpStatusCode.TooManyRequests,
                "rate_limited",
                "valid:repeat-user");
            Assert.True(int.TryParse(second.Headers.RetryAfter?.ToString(), out var retryAfter));
            Assert.InRange(retryAfter, 1, 60);
        }

        var rejected = 0;
        for (var actor = 0; actor < 65; actor++)
        {
            using var request = AuthorizedRequest(
                HttpMethod.Post,
                "write",
                $"valid:cardinality-{actor}");
            using var response = await api.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.TooManyRequests,
                $"Unexpected limiter response {response.StatusCode}.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected++;
        }

        Assert.True(rejected > 0, "More actor identities created unbounded limiter partitions.");
    }

    [Fact]
    public async Task Anonymous_view_limiter_returns_a_canonical_bounded_problem()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(
            identity,
            anonymousViewPermitLimit: 1,
            anonymousViewPartitions: 64);

        using (var first = await api.Client.GetAsync(
            "view",
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await api.Client.GetAsync(
            "view",
            TestContext.Current.CancellationToken);
        await AssertCanonicalProblemAsync(
            second,
            HttpStatusCode.TooManyRequests,
            "rate_limited");
        Assert.True(int.TryParse(second.Headers.RetryAfter?.ToString(), out var retryAfter));
        Assert.InRange(retryAfter, 1, 60);
    }

    [Fact]
    public async Task Deadline_and_client_abort_propagate_cancellation_without_500_or_late_side_effect()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(identity, requestTimeoutSeconds: 1);

        using (var deadlineResponse = await api.Client.GetAsync(
            "deadline",
            TestContext.Current.CancellationToken))
        {
            await AssertCanonicalProblemAsync(
                deadlineResponse,
                HttpStatusCode.GatewayTimeout,
                "request_timeout");
        }

        Assert.True(await api.State.DeadlineCancellation.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));
        Assert.Equal(0, api.State.DeadlineSideEffects);

        using var abortCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        abortCancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            api.Client.GetAsync("abort", abortCancellation.Token));

        Assert.True(await api.State.AbortCancellation.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(0, api.State.AbortSideEffects);
        Assert.DoesNotContain(
            api.Logs.Messages,
            message => message.Contains("completed with 500", StringComparison.OrdinalIgnoreCase));

        var sensitiveRouteValue = "customer-secret-7fef2b2c";
        using var routeResponse = await api.Client.GetAsync(
            $"objects/{sensitiveRouteValue}",
            TestContext.Current.CancellationToken);
        routeResponse.EnsureSuccessStatusCode();
        Assert.Contains(
            api.Logs.Messages,
            message => message.Contains("/objects/{id}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            api.Logs.Messages,
            message => message.Contains(sensitiveRouteValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replica_concurrency_saturation_is_a_retryable_load_shed_not_a_client_quota()
    {
        await using var identity = await IdentityStub.StartAsync();
        await using var api = await ApiHarness.StartAsync(
            identity,
            maxConcurrentRequests: 1);

        var heldRequest = api.Client.GetAsync(
            "concurrency-hold",
            TestContext.Current.CancellationToken);
        try
        {
            await api.State.ConcurrencyEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            using var rejected = await api.Client.GetAsync(
                "context",
                TestContext.Current.CancellationToken);
            await AssertCanonicalProblemAsync(
                rejected,
                HttpStatusCode.ServiceUnavailable,
                "capacity_load_shed");
            Assert.True(int.TryParse(rejected.Headers.RetryAfter?.ToString(), out var retryAfter));
            Assert.InRange(retryAfter, 1, 60);
        }
        finally
        {
            api.State.ConcurrencyRelease.TrySetResult();
        }

        using var completed = await heldRequest;
        completed.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task AssertAuthorizedAsync(ApiHarness api, string token)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, "protected", token);
        using var response = await api.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an accepted token but received {response.StatusCode}; " +
            $"challenge={response.Headers.WwwAuthenticate}.");
    }

    private static async Task WarmIdentityTransportAsync(ApiHarness api)
    {
        const int maximumAttempts = 3;
        const string token = "valid:transport-warmup";

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            using var request = AuthorizedRequest(HttpMethod.Get, "protected", token);
            using var response = await api.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
                return;

            await AssertCanonicalProblemAsync(
                response,
                HttpStatusCode.ServiceUnavailable,
                "identity_validation_unavailable",
                token);
            if (attempt < maximumAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    TestContext.Current.CancellationToken);
            }
        }

        Assert.Fail($"Identity transport did not warm within {maximumAttempts} attempts.");
    }

    private static async Task AssertCanonicalProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string? sensitiveValue = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(IdentityStub.ClientSecret, body, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(sensitiveValue))
            Assert.DoesNotContain(sensitiveValue, body, StringComparison.Ordinal);
    }

    private sealed record ContextSnapshot(
        string Scheme,
        string Host,
        string? RemoteIp,
        bool Authenticated,
        string? Subject);

    private enum SigningKeyVersion
    {
        Old,
        New,
        Unknown,
    }

    private sealed class ApiHarness : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private ApiHarness(
            WebApplication application,
            Uri baseAddress,
            HarnessState state,
            CaptureLoggerProvider logs)
        {
            _application = application;
            BaseAddress = baseAddress;
            State = state;
            Logs = logs;
            Client = new HttpClient
            {
                BaseAddress = baseAddress,
                Timeout = TimeSpan.FromSeconds(10),
            };
        }

        public HttpClient Client { get; }
        public Uri BaseAddress { get; }
        public HarnessState State { get; }
        public CaptureLoggerProvider Logs { get; }

        public static async Task<ApiHarness> StartAsync(
            IdentityStub identity,
            string[]? knownProxies = null,
            int forwardLimit = 2,
            bool fastIdentityFailures = false,
            int writePermitLimit = 60,
            int writePartitions = 4_096,
            int anonymousViewPermitLimit = 600,
            int anonymousViewPartitions = 4_096,
            int requestTimeoutSeconds = 5,
            string validationMode = "Introspection",
            int maxConcurrentRequests = 256)
        {
            var state = new HarnessState();
            var logs = new CaptureLoggerProvider();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(IdentityEdgeAcceptanceTests).Assembly.GetName().Name,
                EnvironmentName = "Development",
            });
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

            var settings = new Dictionary<string, string?>
            {
                ["Runtime:Role"] = ApiRuntimeRole.Api.ToString(),
                ["Identity:ServiceUrl"] = identity.Issuer.ToString(),
                ["Identity:ClientId"] = IdentityStub.ClientId,
                ["Identity:ClientSecret"] = IdentityStub.ClientSecret,
                ["Identity:Audience"] = IdentityStub.ClientId,
                ["Identity:ValidationMode"] = validationMode,
                ["Traffic:ForwardLimit"] = forwardLimit.ToString(),
                ["Traffic:RequestTimeoutSeconds"] = requestTimeoutSeconds.ToString(),
                ["Traffic:MaxConcurrentRequestsPerReplica"] = maxConcurrentRequests.ToString(),
                ["Traffic:AuthenticatedWritePermitLimitPerMinute"] = writePermitLimit.ToString(),
                ["Traffic:AuthenticatedWritePartitionCount"] = writePartitions.ToString(),
                ["Traffic:AnonymousViewPermitLimitPerMinute"] = anonymousViewPermitLimit.ToString(),
                ["Traffic:AnonymousViewPartitionCount"] = anonymousViewPartitions.ToString(),
            };
            if (fastIdentityFailures)
            {
                // Windows' first ephemeral TLS handshake can take around two seconds while
                // initializing the certificate stack. These remain short enough to exercise
                // the bounded timeout without making discovery itself the failure under test.
                settings["Identity:Transport:AttemptTimeoutMilliseconds"] = "2500";
                settings["Identity:Transport:TotalTimeoutMilliseconds"] = "3000";
                settings["Identity:Transport:RetryDelayMilliseconds"] = "10";
            }

            for (var index = 0; index < (knownProxies?.Length ?? 0); index++)
                settings[$"Traffic:KnownProxies:{index}"] = knownProxies![index];
            builder.Configuration.AddInMemoryCollection(settings);

            var host = builder.Api(new ApiOptions
            {
                AppName = "EdgeTest",
                EntryAssembly = typeof(IdentityEdgeAcceptanceTests).Assembly,
                AuthPolicies = [],
                AllowedCorsDomains = ["https://client.test"],
                WithApplication = false,
                WithInfra = false,
                WithCqrs = new CqrsOptions { Enabled = false },
                WithControllers = false,
                WithRouteGroups = false,
                WithBroker = false,
                WithConsumers = false,
                WithJobs = false,
                ConfigureServices = configuredBuilder =>
                {
                    configuredBuilder.Logging.SetMinimumLevel(LogLevel.Trace);
                    configuredBuilder.Logging.AddProvider(logs);
                    configuredBuilder.Services.Configure<OpenIddictValidationSystemNetHttpOptions>(
                        options => options.HttpClientHandlerActions.Add(handler =>
                            handler.ServerCertificateCustomValidationCallback =
                                (_, certificate, _, _) => string.Equals(
                                    certificate?.Thumbprint,
                                    identity.Certificate.Thumbprint,
                                    StringComparison.OrdinalIgnoreCase)));
                },
                ConfigurePipeline = application => MapTestEndpoints(application, state),
            });
            builder.Host.UseSerilog((_, _, configuration) => configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("OpenIddict", LogEventLevel.Warning)
                .WriteTo.Sink(logs));

            var app = host.Build();
            try
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                return new ApiHarness(app, GetAddress(app), state, logs);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.StopAsync();
            await _application.DisposeAsync();
            Logs.Dispose();
        }

        private static void MapTestEndpoints(WebApplication application, HarnessState state)
        {
            application.MapGet("/protected", (HttpContext context) => Results.Json(new
            {
                subject = context.User.FindFirst("sub")?.Value,
            })).RequireAuthorization();
            application.MapGet("/admin", () => Results.Ok())
                .RequireAuthorization(policy => policy.RequireClaim("permission", "admin"));
            application.MapGet("/context", (HttpContext context) => Results.Json(new ContextSnapshot(
                context.Request.Scheme,
                context.Request.Host.ToString(),
                context.Connection.RemoteIpAddress?.ToString(),
                context.User.Identity?.IsAuthenticated == true,
                context.User.FindFirst("sub")?.Value)));
            application.MapPost("/write", () => Results.Ok())
                .RequireAuthorization()
                .RequireRateLimiting(TrafficProtectionPolicies.AuthenticatedWrite);
            application.MapGet("/view", () => Results.Ok())
                .RequireRateLimiting(TrafficProtectionPolicies.AnonymousView);
            application.MapGet("/deadline", async (HttpContext context) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                }
                finally
                {
                    state.DeadlineCancellation.TrySetResult(
                        context.RequestAborted.IsCancellationRequested);
                }

                Interlocked.Increment(ref state.DeadlineSideEffects);
                return Results.Ok();
            });
            application.MapGet("/abort", async (HttpContext context) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                }
                finally
                {
                    state.AbortCancellation.TrySetResult(
                        context.RequestAborted.IsCancellationRequested);
                }

                Interlocked.Increment(ref state.AbortSideEffects);
                return Results.Ok();
            });
            application.MapGet("/concurrency-hold", async (HttpContext context) =>
            {
                state.ConcurrencyEntered.TrySetResult();
                await state.ConcurrencyRelease.Task.WaitAsync(context.RequestAborted);
                return Results.Ok();
            });
            application.MapGet("/objects/{id}", () => Results.Ok());
        }
    }

    public sealed class HarnessState
    {
        public TaskCompletionSource<bool> DeadlineCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AbortCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConcurrencyEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConcurrencyRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DeadlineSideEffects;
        public int AbortSideEffects;
    }

    public sealed class CaptureLoggerProvider : ILoggerProvider, ILogEventSink
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
            new CaptureLogger(_messages);

        public void Emit(LogEvent logEvent)
        {
            var rendered = logEvent.RenderMessage();
            if (logEvent.Exception is not null)
                rendered += Environment.NewLine + logEvent.Exception;
            _messages.Enqueue(rendered);
        }

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(ConcurrentQueue<string> messages)
            : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    private sealed class IdentityStub : IAsyncDisposable
    {
        public const string ClientId = "edge-api";
        public const string ClientSecret = "identity-secret-must-never-leak";
        private readonly WebApplication _application;
        private readonly X509Certificate2 _certificate;
        private readonly RSA _oldSigningKey;
        private readonly RSA _newSigningKey;
        private readonly RSA _unknownSigningKey;
        private int _discoveryRequests;
        private int _jsonWebKeySetRequests;
        private int _outageIntrospectionRequests;
        private int _timeoutIntrospectionRequests;
        private int _rotationStage;

        private IdentityStub(
            WebApplication application,
            Uri issuer,
            X509Certificate2 certificate,
            RSA oldSigningKey,
            RSA newSigningKey,
            RSA unknownSigningKey)
        {
            _application = application;
            _certificate = certificate;
            _oldSigningKey = oldSigningKey;
            _newSigningKey = newSigningKey;
            _unknownSigningKey = unknownSigningKey;
            Issuer = issuer;
        }

        public Uri Issuer { get; }
        public X509Certificate2 Certificate => _certificate;
        public int DiscoveryRequests => Volatile.Read(ref _discoveryRequests);
        public int JsonWebKeySetRequests => Volatile.Read(ref _jsonWebKeySetRequests);
        public int OutageIntrospectionRequests => Volatile.Read(ref _outageIntrospectionRequests);
        public int TimeoutIntrospectionRequests => Volatile.Read(ref _timeoutIntrospectionRequests);

        public void BeginSigningKeyOverlap() => Volatile.Write(ref _rotationStage, 1);

        public void CompleteSigningKeyRotation() => Volatile.Write(ref _rotationStage, 2);

        public string CreateToken(
            SigningKeyVersion keyVersion,
            string subject,
            string? audience = null,
            string? issuer = null,
            DateTimeOffset? expiresAt = null)
        {
            var (key, keyId) = keyVersion switch
            {
                SigningKeyVersion.Old => (_oldSigningKey, "old-key"),
                SigningKeyVersion.New => (_newSigningKey, "new-key"),
                SigningKeyVersion.Unknown => (_unknownSigningKey, "unknown-key"),
                _ => throw new ArgumentOutOfRangeException(nameof(keyVersion)),
            };
            var now = DateTimeOffset.UtcNow;
            var expiration = expiresAt ?? now.AddMinutes(5);
            var issuedAt = expiration <= now ? expiration.AddMinutes(-5) : now;
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer ?? Issuer.ToString(),
                Audience = audience ?? ClientId,
                Subject = new ClaimsIdentity([new Claim("sub", subject)]),
                IssuedAt = issuedAt.UtcDateTime,
                NotBefore = issuedAt.AddMinutes(-1).UtcDateTime,
                Expires = expiration.UtcDateTime,
                TokenType = "at+jwt",
                SigningCredentials = new SigningCredentials(
                    new RsaSecurityKey(key) { KeyId = keyId },
                    SecurityAlgorithms.RsaSha256),
            });
        }

        public static async Task<IdentityStub> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(IdentityEdgeAcceptanceTests).Assembly.GetName().Name,
                EnvironmentName = "Development",
            });
            builder.Logging.ClearProviders();
            var certificate = CreateCertificate();
            var oldSigningKey = RSA.Create(2048);
            var newSigningKey = RSA.Create(2048);
            var unknownSigningKey = RSA.Create(2048);
            builder.WebHost.ConfigureKestrel(options => options.Listen(
                IPAddress.Loopback,
                0,
                listen => listen.UseHttps(certificate)));
            var app = builder.Build();
            IdentityStub? stub = null;

            app.MapGet("/.well-known/openid-configuration", async context =>
            {
                Interlocked.Increment(ref stub!._discoveryRequests);
                await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
                {
                    ["issuer"] = stub.Issuer.ToString(),
                    ["introspection_endpoint"] = new Uri(stub.Issuer, "connect/introspect").ToString(),
                    ["introspection_endpoint_auth_methods_supported"] = new[] { "client_secret_post" },
                    ["jwks_uri"] = new Uri(stub.Issuer, ".well-known/jwks.json").ToString(),
                }, context.RequestAborted);
            });
            app.MapGet("/.well-known/jwks.json", () =>
            {
                Interlocked.Increment(ref stub!._jsonWebKeySetRequests);
                var keys = Volatile.Read(ref stub!._rotationStage) switch
                {
                    0 => new[] { CreateJsonWebKey(stub._oldSigningKey, "old-key") },
                    1 => new[]
                    {
                        CreateJsonWebKey(stub._oldSigningKey, "old-key"),
                        CreateJsonWebKey(stub._newSigningKey, "new-key"),
                    },
                    _ => new[] { CreateJsonWebKey(stub._newSigningKey, "new-key") },
                };
                return Results.Json(new { keys });
            });
            app.MapPost("/connect/introspect", async context =>
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var token = form["token"].ToString();
                var suppliedSecret = form["client_secret"].ToString();
                if (string.IsNullOrEmpty(suppliedSecret))
                    suppliedSecret = ReadBasicSecret(context.Request.Headers.Authorization);
                if (!string.Equals(suppliedSecret, ClientSecret, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "invalid_client" },
                        context.RequestAborted);
                    return;
                }

                if (token == "outage")
                {
                    Interlocked.Increment(ref stub!._outageIntrospectionRequests);
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "temporarily_unavailable" },
                        context.RequestAborted);
                    return;
                }

                if (token == "timeout")
                {
                    Interlocked.Increment(ref stub!._timeoutIntrospectionRequests);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted);
                    }
                    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                    {
                        return;
                    }
                }

                if (token is "not-a-token" or "revoked")
                {
                    await context.Response.WriteAsJsonAsync(
                        new { active = false },
                        context.RequestAborted);
                    return;
                }

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var subject = token.StartsWith("valid:", StringComparison.Ordinal)
                    ? token["valid:".Length..]
                    : token;
                await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
                {
                    ["active"] = true,
                    ["token_type"] = "Bearer",
                    ["client_id"] = ClientId,
                    ["sub"] = subject,
                    ["scope"] = "read write",
                    ["aud"] = token == "wrong-audience" ? "another-api" : ClientId,
                    ["iss"] = token == "wrong-issuer"
                        ? "https://untrusted-issuer.test/"
                        : stub!.Issuer.ToString(),
                    ["iat"] = now - 10,
                    ["exp"] = token == "expired" ? now - 1 : now + 300,
                }, context.RequestAborted);
            });

            try
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                var boundAddress = GetAddress(app);
                var issuer = new UriBuilder(boundAddress) { Host = "localhost" }.Uri;
                stub = new IdentityStub(
                    app,
                    issuer,
                    certificate,
                    oldSigningKey,
                    newSigningKey,
                    unknownSigningKey);
                return stub;
            }
            catch
            {
                await app.DisposeAsync();
                certificate.Dispose();
                oldSigningKey.Dispose();
                newSigningKey.Dispose();
                unknownSigningKey.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
            _certificate.Dispose();
            _oldSigningKey.Dispose();
            _newSigningKey.Dispose();
            _unknownSigningKey.Dispose();
        }

        private static string ReadBasicSecret(string? authorization)
        {
            if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
                || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(header.Parameter))
            {
                return string.Empty;
            }

            try
            {
                var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
                var separator = credentials.IndexOf(':', StringComparison.Ordinal);
                return separator >= 0 ? credentials[(separator + 1)..] : string.Empty;
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static X509Certificate2 CreateCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=localhost",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: false));
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var generated = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddHours(1));
            return X509CertificateLoader.LoadPkcs12(
                generated.Export(X509ContentType.Pfx),
                password: null);
        }

        private static Dictionary<string, object> CreateJsonWebKey(RSA key, string keyId)
        {
            var parameters = key.ExportParameters(includePrivateParameters: false);
            return new Dictionary<string, object>
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["kid"] = keyId,
                ["alg"] = SecurityAlgorithms.RsaSha256,
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
            };
        }
    }

    private static Uri GetAddress(WebApplication application)
    {
        var addresses = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("The test server did not expose an address.");
        return new Uri(addresses.Single(address => address.StartsWith("http", StringComparison.Ordinal)));
    }
}
