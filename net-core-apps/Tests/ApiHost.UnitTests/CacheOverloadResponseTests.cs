using System.Text.Json;
using ApiExceptionHandling;
using Inspacy.Caching;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class CacheOverloadResponseTests
{
    [Fact]
    public async Task Source_budget_rejection_is_a_retryable_503_problem_details_response()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInspacyProblemDetails();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var handler = provider.GetServices<IExceptionHandler>()
            .Single(candidate => candidate.GetType().Name == "CacheSourceOverloadExceptionHandler");
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
        };
        context.Request.Path = "/cache-protected";
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            context,
            new CacheSourceOverloadedException(TimeSpan.FromMilliseconds(50)),
            cancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter);
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: cancellationToken);
        Assert.Equal(
            "cache_source_overloaded",
            problem.RootElement.GetProperty("code").GetString());
    }
}
