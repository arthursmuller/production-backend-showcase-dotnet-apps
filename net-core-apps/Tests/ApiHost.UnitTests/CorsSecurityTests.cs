using InspacyApiHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class CorsSecurityTests
{
    [Fact]
    public void Production_defaults_are_https_and_exclude_loopback_origins()
    {
        var origins = InspacyDefaults.DefaultCorsDomains();

        Assert.NotEmpty(origins);
        Assert.All(origins, origin =>
        {
            var uri = new Uri(origin, UriKind.Absolute);
            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            Assert.False(uri.IsLoopback);
        });
    }

    [Fact]
    public void Development_defaults_explicitly_include_local_origins()
    {
        var origins = InspacyDefaults.DefaultCorsDomains(includeDevelopmentOrigins: true);

        Assert.Contains("http://localhost:3000", origins);
        Assert.Contains("https://www.inspacy.com", origins);
    }

    [Theory]
    [InlineData("http://client.example")]
    [InlineData("https://localhost:3000")]
    public void Production_host_rejects_insecure_or_loopback_origins(string origin)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CorsSecurityTests).Assembly.GetName().Name,
            EnvironmentName = "Production",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Runtime:Role"] = ApiRuntimeRole.Api.ToString(),
            ["Identity:ServiceUrl"] = "https://identity.test/",
            ["Identity:ClientId"] = "test_api",
            ["Identity:ClientSecret"] = "test-only-secret",
            ["Identity:ValidationMode"] = "Introspection",
        });

        var error = Assert.Throws<InvalidOperationException>(() => builder.Api(new ApiOptions
        {
            AppName = "CorsSecurityTestHost",
            EntryAssembly = typeof(CorsSecurityTests).Assembly,
            AuthPolicies = [],
            AllowedCorsDomains = [origin],
            WithApplication = false,
            WithInfra = false,
            WithCqrs = new CqrsOptions { Enabled = false },
            WithControllers = false,
            WithRouteGroups = false,
        }));

        Assert.Contains("non-loopback HTTPS", error.Message, StringComparison.Ordinal);
    }
}
