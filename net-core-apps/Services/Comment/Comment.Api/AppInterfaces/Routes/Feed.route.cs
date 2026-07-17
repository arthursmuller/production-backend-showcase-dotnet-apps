using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StarterEntities;

namespace Comment.API.AppInterfaces.Routes
{
    public static class FeedApiEndpoints
    {
        public static IEndpointRouteBuilder MapFeedRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/feed/v1").WithTags("Feed");

            group.MapGet("/visibility-level", VisibilityLevels);

            return routes;
        }

        internal static IResult VisibilityLevels()
            => Results.Ok(new Response<System.Collections.Generic.IEnumerable<VisibilityLevel>>(VisibilityLevel.List()));
    }
}
