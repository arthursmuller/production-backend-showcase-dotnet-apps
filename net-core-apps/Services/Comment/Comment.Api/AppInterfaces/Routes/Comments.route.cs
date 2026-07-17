using System.Collections.Generic;
using System.Threading.Tasks;
using Comment.Application.Dtos;
using Comment.Application.Features.Comments.Commands;
using Comment.API.Features.Comments;
using Comment.Domain.AggregatesModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StarterEntities;

namespace Comment.API.AppInterfaces.Routes
{
    public static class CommentsApiEndpoints
    {
        public static IEndpointRouteBuilder MapCommentsRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/comments/v1").WithTags("Comments").RequireAuthorization();

            group.MapGet("/{id:int}", GetById).AllowAnonymous()
                 .Produces<Response<CommentPostDto>>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapGet("/owner/{id:int}", GetOwnerById)
                 .Produces<Response<CommentPost>>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapPost("/list", List).AllowAnonymous()
                 .Produces<PagedResponse<IEnumerable<CommentPostDto>>>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapPost("/owner/list", ListOwner)
                 .Produces<PagedResponse<IEnumerable<CommentPost>>>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapDelete("/{id:int}", Remove)
                 .Produces<bool>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            return routes;
        }

        internal static async Task<IResult> GetById(int id, [FromServices] GetCommentByIdQuery h)
            => Results.Ok(new Response<CommentPostDto>(await h.Handle(id)));

        internal static async Task<IResult> GetOwnerById(int id, [FromServices] GetCommentOwnerByIdQuery h)
            => Results.Ok(new Response<CommentPost>(await h.Handle(id)));

        internal static async Task<IResult> List(CommentsCriteria criteria, [FromServices] ListCommentsQuery h)
        {
            var (data, total) = await h.Handle(criteria);
            return Results.Ok(PaginationFactory.CreatePagedReponse(data, criteria.PageNumber ?? 1, criteria.PageSize ?? 10, total));
        }

        internal static async Task<IResult> ListOwner(CommentsCriteria criteria, [FromServices] ListOwnerCommentsQuery h)
        {
            var (data, total) = await h.Handle(criteria);
            return Results.Ok(PaginationFactory.CreatePagedReponse(data, criteria.PageNumber ?? 1, criteria.PageSize ?? 10, total));
        }

        internal static async Task<IResult> Remove(int id, [FromServices] RemoveCommentCommand h)
            => Results.Ok(await h.Handle(id));
    }
}
