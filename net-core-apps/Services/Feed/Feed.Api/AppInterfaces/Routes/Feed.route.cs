using System.Collections.Generic;
using System.Threading.Tasks;
using Feed.API.Features.Feeds;
using Feed.Application.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using StarterEntities;


namespace Feed.API.AppInterfaces.Routes
{
    public static class FeedApiEndpoints
    {
        public static IEndpointRouteBuilder MapFeedRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/feed/v1").WithTags("Feed");

            group.MapGet("/visibility-level", VisibilityLevels)
                 .WithName("FeedVisibilityLevels")
                 .AllowAnonymous()
                 .Produces<Response<IEnumerable<VisibilityLevel>>>(StatusCodes.Status200OK);

            group.MapGet("/user/auth/list-viewed", ListViewed)
                 .WithName("FeedListViewed")
                 .RequireAuthorization()
                 .Produces<PagedResponse<IEnumerable<UserConnectionsFeedDto>>>(StatusCodes.Status200OK);

            group.MapGet("/user/auth/list-not-viewed", ListNotViewed)
                 .WithName("FeedListNotViewed")
                 .RequireAuthorization()
                 .Produces<PagedResponse<IEnumerable<UserConnectionsFeedDto>>>(StatusCodes.Status200OK);

            return routes;
        }

        internal static IResult VisibilityLevels()
        {
            try { return Results.Ok(R.Success(VisibilityLevel.List())); }
            catch { return Results.Problem("Failed to list visibility levels.", statusCode: 500); }
        }

        internal static async Task<IResult> ListViewed([AsParameters] FeedViewedCriteria criteria,
                                                       [FromServices] ListMyFeedViewedQuery h)
        {
            try { return Results.Ok(PaginationFactory.CreatePagedReponse(await h.Handle(criteria), criteria.PageNumber, criteria.PageSize)); }
            catch { return Results.Problem("Failed to list viewed feed items.", statusCode: 500); }
        }

        internal static async Task<IResult> ListNotViewed([FromServices] ListMyFeedNotViewedQuery h)
        {
            try { return Results.Ok(PaginationFactory.CreatePagedReponse(await h.Handle(), null, null)); }
            catch { return Results.Problem("Failed to list not-viewed feed items.", statusCode: 500); }
        }
    }
}
