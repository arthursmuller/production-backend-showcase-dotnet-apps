using System.Collections.Generic;
using System.Threading.Tasks;
using Feed.API.Features.Posts;
using Feed.Application.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;


namespace Feed.API.AppInterfaces.Routes
{
    public static class PostApiEndpoints
    {
        public static IEndpointRouteBuilder MapPostRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/post/v1").WithTags("Post");

            group.MapGet("/user/list/{userId:int}", ListUserPosts)
                 .WithName("PostListUserPosts")
                 .AllowAnonymous()
                 .Produces<PagedResponse<IEnumerable<PostsCriteria>>>(StatusCodes.Status200OK);

            group.MapGet("/user/auth/list", ListMyPosts)
                 .WithName("PostListMyPosts")
                 .RequireAuthorization()
                 .Produces<PagedResponse<IEnumerable<PostsCriteria>>>(StatusCodes.Status200OK);

            return routes;
        }

        internal static async Task<IResult> ListUserPosts([AsParameters] PostsCriteria criteria,
                                                          [FromRoute] int userId,
                                                          [FromServices] ListUserPostsQuery h)
        {
            try { 
                criteria.UserId = userId; 
                return Results.Ok(PaginationFactory.CreatePagedReponse(await h.Handle(criteria), criteria.PageNumber, criteria.PageSize)); 
            }
            catch { return Results.Problem("Failed to list user posts.", statusCode: 500); }
        }

        internal static async Task<IResult> ListMyPosts([AsParameters] PostsCriteria criteria,
                                                        [FromServices] ListMyPostsQuery h)
        {
            try { return Results.Ok(PaginationFactory.CreatePagedReponse(await h.Handle(criteria), criteria.PageNumber, criteria.PageSize)); }
            catch { return Results.Problem("Failed to list my posts.", statusCode: 500); }
        }
    }
}
