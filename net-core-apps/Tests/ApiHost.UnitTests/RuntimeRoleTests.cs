using InspacyApiHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ApiHost.UnitTests;

public sealed class RuntimeRoleTests
{
    [Fact]
    public async Task Api_role_exposes_business_and_health_routes_without_workers_or_migrations()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.Api);
        var options = CreateOptions();

        var host = builder.Api(options);

        Assert.Equal(ApiRuntimeRole.Api, options.RuntimeRole);
        Assert.True(options.WithRouteGroups);
        Assert.False(options.WithConsumers);
        Assert.False(options.WithJobs);
        Assert.False(options.WithDatabases.Enabled);

        await using var app = host.Build();
        var routePatterns = ((IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/_runtime-role-probe", routePatterns);
        Assert.Contains("/health/live", routePatterns);
        Assert.Contains("/health/ready", routePatterns);
        Assert.Contains("/api/healthcheck", routePatterns);
        Assert.DoesNotContain(
            app.Services.GetServices<IHostedService>(),
            service => service is RuntimeRoleProbeJob);
    }

    [Theory]
    [InlineData(ApiRuntimeRole.JobWorker)]
    [InlineData(ApiRuntimeRole.Migration)]
    public void Non_request_roles_do_not_expose_discovered_business_routes(ApiRuntimeRole role)
    {
        var builder = CreateBuilder("Production", role);
        var options = CreateOptions();

        builder.Api(options);

        Assert.False(options.WithRouteGroups);
        Assert.False(options.WithControllers);
        Assert.Equal(role == ApiRuntimeRole.Migration, options.WithDatabases.Enabled);
    }

    [Theory]
    [InlineData(ApiRuntimeRole.JobWorker)]
    [InlineData(ApiRuntimeRole.Migration)]
    public async Task Non_request_roles_do_not_bind_the_api_identity_secret(ApiRuntimeRole role)
    {
        var builder = CreateBuilder("Production", role);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:ClientSecret"] = "{{secret:IdentityClientSecret}}",
        });

        var host = builder.Api(CreateOptions());
        await using var app = await host.BuildAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(app);
    }

    [Fact]
    public void Consumer_role_enables_receive_endpoints_but_not_business_routes()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.ConsumerWorker);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Broker:Provider"] = "MassTransit",
            ["Broker:Transport"] = "RabbitMq",
            ["Broker:RabbitMq:host"] = "127.0.0.1",
            ["Broker:RabbitMq:port"] = "5672",
            ["Broker:RabbitMq:username"] = "test",
            ["Broker:RabbitMq:password"] = "test",
            ["Broker:RabbitMq:virtualHost"] = "test",
            ["Broker:RabbitMq:Tls:Enabled"] = "false",
        });
        var options = CreateOptions();

        builder.Api(options);

        Assert.True(options.WithBroker);
        Assert.True(options.WithConsumers);
        Assert.False(options.WithRouteGroups);
        Assert.False(options.WithJobs);
        Assert.False(options.WithDatabases.Enabled);
    }

    [Fact]
    public async Task Consumer_worker_build_exposes_only_operational_routes()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.ConsumerWorker);
        AddBrokerConfiguration(builder.Configuration);
        var options = CreateOptions();

        var host = builder.Api(options);
        await using var app = host.Build();
        var routePatterns = ((IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            routePatterns.SetEquals(new HashSet<string?>(StringComparer.Ordinal)
            {
                "/health/live",
                "/health/ready",
                "/api/healthcheck",
            }));
        Assert.DoesNotContain("/_runtime-role-probe", routePatterns);
    }

    [Fact]
    public void Worker_role_rejects_a_business_route_added_through_the_pipeline_callback()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.JobWorker);
        var options = CreateOptions();
        options.ConfigurePipeline = app => app.MapGet("/pipeline-bypass", () => "unsafe");
        var host = builder.Api(options);

        var error = Assert.Throws<InvalidOperationException>(() => host.Build());

        Assert.Contains("cannot expose non-operational routes", error.Message, StringComparison.Ordinal);
        Assert.Contains("/pipeline-bypass", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_stopping_marks_readiness_unhealthy_before_host_stop()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.JobWorker);
        var options = CreateOptions();
        options.WithJobs = false;
        var host = builder.Api(options);
        host.Builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = host.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var checks = app.Services.GetRequiredService<HealthCheckService>();
        var before = await checks.CheckHealthAsync(
            registration => registration.Name == "runtime-drain",
            TestContext.Current.CancellationToken);
        Assert.Equal(HealthStatus.Healthy, before.Status);

        app.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        var duringDrain = await checks.CheckHealthAsync(
            registration => registration.Name == "runtime-drain",
            TestContext.Current.CancellationToken);
        Assert.Equal(HealthStatus.Unhealthy, duringDrain.Status);

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Job_worker_shutdown_propagates_the_stopping_token_to_application_jobs()
    {
        RuntimeRoleProbeJob.Reset();
        var builder = CreateBuilder("Production", ApiRuntimeRole.JobWorker);
        var host = builder.Api(CreateOptions());
        host.Builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = host.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await RuntimeRoleProbeJob.Started.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(await RuntimeRoleProbeJob.StoppedByCancellation.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Job_role_auto_discovers_concrete_background_services()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.JobWorker);
        var options = CreateOptions();

        var host = builder.Api(options);

        Assert.True(options.WithJobs);
        await using var app = host.Build();
        Assert.Contains(
            app.Services.GetServices<IHostedService>(),
            service => service is RuntimeRoleProbeJob);
    }

    [Fact]
    public void Combined_role_is_rejected_outside_development()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.Combined);
        var options = CreateOptions();

        var error = Assert.Throws<InvalidOperationException>(() => builder.Api(options));

        Assert.Contains("restricted to Development", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_role_rejects_receive_endpoints_even_when_broker_is_enabled()
    {
        var builder = CreateBuilder("Production", ApiRuntimeRole.Api);
        var options = CreateOptions();
        options.WithBroker = true;
        options.WithConsumers = true;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Api(options));

        Assert.Contains("cannot start receive endpoints", error.Message, StringComparison.Ordinal);
    }

    private static WebApplicationBuilder CreateBuilder(string environment, ApiRuntimeRole role)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RuntimeRoleTests).Assembly.GetName().Name,
            EnvironmentName = environment,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Runtime:Role"] = role.ToString(),
            ["Identity:ServiceUrl"] = "https://identity.test/",
            ["Identity:ClientId"] = "test_api",
            ["Identity:ClientSecret"] = "not-a-production-secret",
            ["Identity:ValidationMode"] = "Introspection",
        });
        return builder;
    }

    private static ApiOptions CreateOptions() => new()
    {
        AppName = "TestHost",
        EntryAssembly = typeof(RuntimeRoleTests).Assembly,
        AuthPolicies = [],
        AllowedCorsDomains = ["https://client.test"],
        WithApplication = false,
        WithInfra = false,
        WithCqrs = new CqrsOptions { Enabled = false },
        WithControllers = false,
    };

    private static void AddBrokerConfiguration(ConfigurationManager configuration)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Broker:Provider"] = "MassTransit",
            ["Broker:Transport"] = "RabbitMq",
            ["Broker:RabbitMq:host"] = "127.0.0.1",
            ["Broker:RabbitMq:port"] = "5672",
            ["Broker:RabbitMq:username"] = "runtime-role-probe",
            ["Broker:RabbitMq:password"] = $"ephemeral-{Guid.NewGuid():N}",
            ["Broker:RabbitMq:virtualHost"] = "runtime-role-probe",
            ["Broker:RabbitMq:Tls:Enabled"] = "false",
        });
    }
}

public static class RuntimeRoleProbeRoutes
{
    public static IEndpointRouteBuilder MapRuntimeRoleProbeRoutes(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/_runtime-role-probe", () => "ok");
        return routes;
    }
}

public sealed class RuntimeRoleProbeJob : BackgroundService
{
    private static TaskCompletionSource _started = NewCompletion();
    private static TaskCompletionSource<bool> _stoppedByCancellation = NewBooleanCompletion();

    public static Task Started => Volatile.Read(ref _started).Task;
    public static Task<bool> StoppedByCancellation =>
        Volatile.Read(ref _stoppedByCancellation).Task;

    public static void Reset()
    {
        Volatile.Write(ref _started, NewCompletion());
        Volatile.Write(ref _stoppedByCancellation, NewBooleanCompletion());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Volatile.Read(ref _started).TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Volatile.Read(ref _stoppedByCancellation).TrySetResult(true);
        }
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> NewBooleanCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
