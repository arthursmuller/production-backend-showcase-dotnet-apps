using Comment.API.AppInterfaces.Routes;
using Comment.Application.Dtos;
using CommomStartupExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentRouteContractTests
{
    [Fact]
    public async Task Canonical_route_set_is_bounded_and_documents_throttling()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.MapCommentsRoutes();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ThenBy(endpoint => HttpMethod(endpoint), StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(5, endpoints.Length);
        Assert.All(endpoints, endpoint =>
        {
            Assert.StartsWith("/api/comments/", endpoint.RoutePattern.RawText,
                StringComparison.Ordinal);
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status429TooManyRequests &&
                    metadata.ContentTypes.Contains("application/problem+json"));
        });

        Assert.All(endpoints.Where(endpoint => HttpMethod(endpoint) == HttpMethods.Get), endpoint =>
            Assert.Equal(
                TrafficProtectionPolicies.AnonymousView,
                endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
        Assert.Equal(
            TrafficProtectionPolicies.AuthenticatedWrite,
            Assert.Single(endpoints, endpoint => HttpMethod(endpoint) == HttpMethods.Delete)
                .Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
        var media = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/api/comments/{id:int}/media/{mediaItemId:int}");
        Assert.NotNull(media.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Cursor_query_binder_normalizes_bounded_filters()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(
            "?PageSize=25&Ids=3,2,3&Targets[0].TargetId=%20post-7%20&" +
            "Targets[0].TargetType=%20POST%20");

        var bound = await CommentsCriteriaQuery.BindAsync(context, null);

        Assert.Equal(25, bound.Criteria.PageSize);
        var ids = Assert.IsType<int[]>(bound.Criteria.Ids);
        Assert.Equal([3, 2], ids);
        var targets = Assert.IsType<CommentTargetFilter[]>(bound.Criteria.Targets);
        var target = Assert.Single(targets);
        Assert.Equal("post-7", target.TargetId);
        Assert.Equal("post", target.TargetType);
    }

    [Theory]
    [InlineData("PageNumber=2")]
    [InlineData("Search=text")]
    [InlineData("SortBy=trending")]
    [InlineData("PageSize=51")]
    [InlineData("RelatedEntityProps[0].Id=post-7&RelatedEntityProps[0].EntityType=post")]
    [InlineData("Targets.TargetId=post-7&Targets.TargetType=post")]
    [InlineData("Targets[0][TargetId]=post-7&Targets[0][TargetType]=post")]
    public async Task Non_cursor_or_unbounded_query_inputs_are_rejected(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?{query}");

        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            CommentsCriteriaQuery.BindAsync(context, null!).AsTask());

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    private static string HttpMethod(RouteEndpoint endpoint) =>
        Assert.Single(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
}
