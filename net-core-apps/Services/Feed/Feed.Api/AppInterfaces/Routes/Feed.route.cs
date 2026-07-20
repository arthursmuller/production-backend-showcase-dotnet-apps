using System.Threading;
using System.Threading.Tasks;
using Feed.Application.Features.Feeds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Feed.API.AppInterfaces.Routes
{
    public static class FeedApiEndpoints
    {
        public static IEndpointRouteBuilder MapFeedRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/feed").WithTags("Feed");
            group.MapGet("/user/auth/items", List)
                .WithName("FeedList")
                .RequireAuthorization()
                .Produces<FeedPage>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
            group.MapPost("/user/auth/seen", MarkSeen)
                .WithName("FeedMarkSeen")
                .RequireAuthorization()
                .Accepts<MarkFeedActivitiesSeen>("application/json")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
                .ProducesProblem(StatusCodes.Status500InternalServerError);

            return routes;
        }

        internal static async Task<IResult> List(
            [FromServices] ListFeedQuery query,
            CancellationToken cancellationToken,
            string? cursor = null,
            int pageSize = 20,
            bool includeSeen = true)
        {
            return Results.Ok(await query.Handle(
                new FeedPageRequest(cursor, pageSize, includeSeen),
                cancellationToken));
        }

        internal static async Task<IResult> MarkSeen(
            [FromBody] MarkFeedActivitiesSeen request,
            [FromServices] MarkFeedActivitiesSeenCommand command,
            CancellationToken cancellationToken)
        {
            await command.Handle(request, cancellationToken);
            return Results.NoContent();
        }
    }
}
