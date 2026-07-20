using System.Net;
using System.Text.Json;
using InspacyApiHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class QueryBindingContractTests
{
    [Fact]
    public async Task Blank_query_values_reach_endpoint_binding_without_being_rewritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(QueryBindingContractTests).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Runtime:Role"] = ApiRuntimeRole.Api.ToString(),
            ["Identity:ServiceUrl"] = "https://identity.test/",
            ["Identity:ClientId"] = "query-binding-probe",
            ["Identity:ClientSecret"] = $"ephemeral-{Guid.NewGuid():N}",
            ["Identity:ValidationMode"] = "Introspection",
        });

        var host = builder.Api(new ApiOptions
        {
            AppName = "QueryBindingProbe",
            EntryAssembly = typeof(QueryBindingContractTests).Assembly,
            RuntimeRole = ApiRuntimeRole.Api,
            AuthPolicies = [],
            AllowedCorsDomains = ["https://client.test"],
            WithApplication = false,
            WithInfra = false,
            WithCqrs = new CqrsOptions { Enabled = false },
            WithControllers = false,
            WithRouteGroups = true,
            WithBroker = false,
            WithConsumers = false,
            WithJobs = false,
        });

        await using var app = host.Build();
        await app.StartAsync(cancellationToken);
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses
                ?? throw new InvalidOperationException("The query-binding probe did not expose an address.");
            using var client = new HttpClient
            {
                BaseAddress = new Uri(addresses.Single(address =>
                    address.StartsWith("http", StringComparison.Ordinal))),
            };

            using var rawResponse = await client.GetAsync(
                "/_query-raw-probe?value=",
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, rawResponse.StatusCode);
            await using var body = await rawResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(
                body,
                cancellationToken: cancellationToken);
            Assert.True(json.RootElement.GetProperty("present").GetBoolean());
            Assert.Equal(string.Empty, json.RootElement.GetProperty("value").GetString());

            using var typedResponse = await client.GetAsync(
                "/_query-int-probe?count=",
                cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, typedResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync(cancellationToken);
        }
    }
}

public static class QueryBindingContractProbeRoutes
{
    public static IEndpointRouteBuilder MapQueryBindingContractProbeRoutes(
        this IEndpointRouteBuilder routes)
    {
        routes.MapGet(
            "/_query-raw-probe",
            (HttpRequest request) => Results.Json(new
            {
                Present = request.Query.ContainsKey("value"),
                Value = request.Query["value"].ToString(),
            }));
        routes.MapGet("/_query-int-probe", (int? count) => Results.Ok(new { Count = count }));
        return routes;
    }
}
