using System.Threading.Tasks;
using Comment.Application.Features.SocialInteractions.Commands;
using Comment.API.Features.SocialInteractions;
using Comment.Domain.AggregatesModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StarterEntities;

namespace Comment.API.AppInterfaces.Routes
{
    public static class SocialInteractionsApiEndpoints
    {
        public static IEndpointRouteBuilder MapSocialInteractionsRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/socialinteractions/v1").WithTags("SocialInteractions").RequireAuthorization();

            group.MapPost("/view", AddView).AllowAnonymous()
                 .Accepts<AddViewDto>("application/json")
                 .Produces(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapGet("/rating/{id:int}", GetRatingById)
                 .Produces<Response<CommentSocialRating>>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapGet("/rating-level/{id:int}", GetRatingLevelById)
                 .Produces<Response<CommomRatingLevel>>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapGet("/report/{id:int}", GetReportById)
                 .Produces<Response<CommomReport>>(StatusCodes.Status200OK)
                 .Produces<Response<string>>(StatusCodes.Status500InternalServerError);

            group.MapPost("/like", Like);
            group.MapPost("/dislike", Dislike);
            group.MapPost("/report", Report);
            group.MapPost("/report-add-comment", ReportAddComment);
            group.MapPost("/rating-level", AddRatingLevel);

            return routes;
        }

        internal static async Task<IResult> AddView(AddViewDto dto, [FromServices] AddViewCommand h)
        {
            await h.Handle(dto);
            return Results.Ok();
        }

        internal static async Task<IResult> GetRatingById(int id, [FromServices] GetRatingQuery h)
            => Results.Ok(new Response<CommentSocialRating>(await h.Handle(id)));

        internal static async Task<IResult> GetRatingLevelById(int id, [FromServices] GetRatingLevelQuery h)
            => Results.Ok(new Response<CommomRatingLevel>(await h.Handle(id)));

        internal static async Task<IResult> GetReportById(int id, [FromServices] GetReportQuery h)
            => Results.Ok(new Response<CommomReport>(await h.Handle(id)));

        internal static async Task<IResult> Like(LikeDto dto, [FromServices] LikeCommand h)
        {
            await h.Handle(dto);
            return Results.Ok();
        }

        internal static async Task<IResult> Dislike(DislikeDto dto, [FromServices] DislikeCommand h)
            => Results.Ok(new Response<CommentSocialAnalytics>(await h.Handle(dto)));

        internal static async Task<IResult> Report(CommomReportDto dto, [FromServices] ReportCommand h)
            => Results.Ok(new Response<CommentSocialAnalytics>(await h.Handle(dto)));

        internal static async Task<IResult> ReportAddComment(ReportAddCommentDto dto, [FromServices] ReportAddCommentCommand h)
            => Results.Ok(new Response<CommentSocialAnalytics>(await h.Handle(dto)));

        internal static async Task<IResult> AddRatingLevel(AddRatingLevelDto dto, [FromServices] AddRatingLevelCommand h)
            => Results.Ok(new Response<CommentSocialAnalytics>(await h.Handle(dto)));
    }
}
