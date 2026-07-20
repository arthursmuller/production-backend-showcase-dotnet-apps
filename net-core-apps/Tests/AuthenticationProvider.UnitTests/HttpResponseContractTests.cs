using System.Net;
using System.Text;
using System.Text.Json;
using AuthenticationProvider.Providers.Api;
using Xunit;

namespace AuthenticationProvider.UnitTests;

public sealed class HttpResponseContractTests
{
    [Fact]
    public async Task Successful_json_null_response_is_rejected_as_a_protocol_error()
    {
        using var client = new HttpClient(new StubHandler())
        {
            BaseAddress = new Uri("https://identity.example.test/"),
        };
        var provider = new AuthenticationApiProvider(
            client,
            new AccessTokenCache(TimeProvider.System));

        var error = await Assert.ThrowsAsync<JsonException>(() => provider.GetAccessToken(
            "client",
            "secret",
            ["read"],
            TestContext.Current.CancellationToken));

        Assert.Contains("JSON null", error.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, error.Data["status_code"]);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json"),
            });
    }
}
