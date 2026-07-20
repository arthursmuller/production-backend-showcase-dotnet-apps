using Interaction.Api.AppInterfaces.Routes;
using Interaction.Api;
using Interaction.Application.Features.Exports;
using Interaction.Application.Options;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Interaction.UnitTests;

public sealed class InteractionApiContractTests
{
    [Fact]
    public async Task Routes_have_documented_auth_and_status_semantics()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.MapInteractionRoutes();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();

        var engagement = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/resources/{type}/{id}/engagement");
        Assert.Contains(engagement.Metadata, metadata => metadata is IAllowAnonymous);

        var views = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/resources/{type}/{id}/views");
        Assert.Contains(views.Metadata, metadata => metadata is IAuthorizeData);
        Assert.Contains(views.Metadata.OfType<IProducesResponseTypeMetadata>(), metadata => metadata.StatusCode == 202);

        var batch = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/interactions/status:batch");
        Assert.Contains(batch.Metadata, metadata => metadata is IAuthorizeData);

        var export = Assert.Single(endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/internal/interaction/authoritative-state");
        Assert.Contains(export.Metadata.OfType<IAuthorizeData>(),
            metadata => metadata.Policy == InteractionPolicies.AuthoritativeExport);
        Assert.Contains(export.Metadata.OfType<IProducesResponseTypeMetadata>(),
            metadata => metadata.StatusCode == StatusCodes.Status403Forbidden);
        Assert.Equal(8, endpoints.Length);
    }

    [Fact]
    public async Task Authority_export_policy_requires_a_dedicated_service_client_scope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInteractionAuthorization(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var service = Principal(
            new Claim("client_id", "feed-projection"),
            new Claim("scope", InteractionAuthorization.DefaultExportScope));
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim("scope", InteractionAuthorization.DefaultExportScope));
        var wrongScope = Principal(
            new Claim("client_id", "feed-projection"),
            new Claim("scope", "interaction.read"));

        Assert.True((await authorization.AuthorizeAsync(
            service, null, InteractionPolicies.AuthoritativeExport)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            user, null, InteractionPolicies.AuthoritativeExport)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            wrongScope, null, InteractionPolicies.AuthoritativeExport)).Succeeded);
    }

    [Theory]
    [InlineData("ConsumerWorker")]
    [InlineData("JobWorker")]
    [InlineData("Migration")]
    public void Non_request_service_entry_roles_do_not_register_or_require_export_cursor_secrets(
        string role)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Runtime:Role"] = role,
            ["AuthoritativeExport:CursorSigningKey"] = string.Empty,
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInteractionAuthorization(configuration);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(InteractionAuthoritativeExportCursor));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Combined")]
    public void Request_service_entry_roles_register_and_validate_export_cursor_secrets(string role)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Runtime:Role"] = role,
            ["AuthoritativeExport:CursorSigningKey"] = Convert.ToBase64String(new byte[32]),
            ["AuthoritativeExport:CursorLifetimeMinutes"] = "360",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInteractionAuthorization(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<InteractionAuthoritativeExportCursor>());
        Assert.True(AuthoritativeExportOptions.IsValid(
            provider.GetRequiredService<IOptions<AuthoritativeExportOptions>>().Value));
    }

    [Fact]
    public void Local_and_production_hosts_wire_api_only_authority_cursor_secret_sources()
    {
        var root = FindNetCoreRoot();
        var environment = File.ReadAllText(Path.Combine(root, "DevOps", ".env.example"));
        var compose = File.ReadAllText(Path.Combine(root, "DevOps", "docker-compose.override.yml"));
        var production = File.ReadAllText(Path.Combine(
            root,
            "DevOps",
            "environments",
            "production",
            "interaction.values.example.yaml"));

        Assert.Contains("INTERACTION_AUTHORITY_EXPORT_CURSOR_SIGNING_KEY=", environment, StringComparison.Ordinal);
        Assert.Contains("AuthoritativeExport__CursorSigningKey=${INTERACTION_AUTHORITY_EXPORT_CURSOR_SIGNING_KEY", compose, StringComparison.Ordinal);
        Assert.Contains("interaction-api-runtime must expose AuthoritativeExport__CursorSigningKey", production, StringComparison.Ordinal);
        Assert.Contains("Worker and migration Secrets must omit it", production, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string FindNetCoreRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "DevOps")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the net-core-apps source root.");
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
