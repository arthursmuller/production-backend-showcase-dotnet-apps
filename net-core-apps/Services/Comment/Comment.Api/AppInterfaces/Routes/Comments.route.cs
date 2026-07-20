using Comment.Application.Dtos;
using Comment.Application.Features.Comments.Commands;
using Comment.Application.Features.Comments.Queries;
using CommomStartupExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Routes
{
    public static class CommentsApiEndpoints
    {
        public static IEndpointRouteBuilder MapCommentsRoutes(this IEndpointRouteBuilder routes)
        {
            var cursorGroup = routes
                .MapGroup("/api/comments")
                .WithTags("Comments")
                .RequireAuthorization();

            cursorGroup.MapGet("/list", ListCursor)
                .AllowAnonymous()
                .RequireRateLimiting(TrafficProtectionPolicies.AnonymousView)
                .WithName("ListCommentsCursor")
                .WithSummary("Lists public comments with stable keyset pagination.")
                .WithDescription(
                    "Returns at most 50 items ordered by CreatedDate and Id descending. " +
                    "Pass NextCursor to continue. PageNumber, Search, trending, and total counts are intentionally unsupported.")
                .Produces<CommentCursorPage<CommentCursorItemDto>>(StatusCodes.Status200OK)
                .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
                .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

            cursorGroup.MapGet("/owner/list", ListOwnerCursor)
                .RequireRateLimiting(TrafficProtectionPolicies.AnonymousView)
                .WithName("ListOwnerCommentsCursor")
                .WithSummary("Lists the authenticated owner's comments with stable keyset pagination.")
                .WithDescription(
                    "Returns at most 50 items ordered by CreatedDate and Id descending. " +
                    "The signed cursor is bound to the authenticated owner and original filters.")
                .Produces<CommentCursorPage<CommentCursorItemDto>>(StatusCodes.Status200OK)
                .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
                .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
                .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
                .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

            cursorGroup.MapGet("/{id:int}", GetById)
                .AllowAnonymous()
                .RequireRateLimiting(TrafficProtectionPolicies.AnonymousView)
                .WithName("GetCommentById")
                .WithSummary("Returns one public comment representation.")
                .Produces<Response<CommentCursorItemDto>>(StatusCodes.Status200OK)
                .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
                .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

            cursorGroup.MapGet("/{id:int}/media/{mediaItemId:int}", GetMedia)
                .AllowAnonymous()
                .RequireRateLimiting(TrafficProtectionPolicies.AnonymousView)
                .WithName("GetCommentMedia")
                .WithSummary("Streams comment media after evaluating its current audience policy.")
                .Produces(StatusCodes.Status200OK)
                .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
                .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

            cursorGroup.MapDelete("/{id:int}", Remove)
                .RequireRateLimiting(TrafficProtectionPolicies.AuthenticatedWrite)
                .WithName("DeleteComment")
                .Produces(StatusCodes.Status204NoContent)
                .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
                .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
                .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

            return routes;
        }

        internal static async Task<IResult> ListCursor(
            CommentsCriteriaQuery request,
            [FromServices] ListCommentsCursorQuery handler,
            CancellationToken cancellationToken,
            [FromQuery] string? cursor = null)
        {
            var validationError = ValidateCursorRequest(request.Criteria, cursor);
            if (validationError is not null)
                return InvalidCriteria("The comment cursor request is invalid.", validationError);
            var result = await handler.Handle(
                new CommentCursorRequest(request.Criteria, cursor),
                cancellationToken);
            return Results.Ok(result);
        }

        internal static async Task<IResult> ListOwnerCursor(
            CommentsCriteriaQuery request,
            [FromServices] ListOwnerCommentsCursorQuery handler,
            CancellationToken cancellationToken,
            [FromQuery] string? cursor = null)
        {
            var validationError = ValidateCursorRequest(request.Criteria, cursor);
            if (validationError is not null)
                return InvalidCriteria("The comment cursor request is invalid.", validationError);
            var result = await handler.Handle(
                new CommentCursorRequest(request.Criteria, cursor),
                cancellationToken);
            return Results.Ok(result);
        }

        internal static async Task<IResult> GetById(
            int id,
            [FromServices] GetCommentByIdQuery handler,
            CancellationToken cancellationToken)
        {
            var comment = await handler.Handle(id, cancellationToken);
            return comment is null
                ? Results.NotFound()
                : Results.Ok(new Response<CommentCursorItemDto>(comment));
        }

        internal static async Task<IResult> Remove(
            int id,
            [FromServices] RemoveCommentCommand handler,
            CancellationToken cancellationToken)
        {
            var removed = await handler.Handle(id, cancellationToken);
            return removed ? Results.NoContent() : Results.NotFound();
        }

        internal static async Task<IResult> GetMedia(
            int id,
            int mediaItemId,
            HttpContext httpContext,
            [FromServices] GetCommentMediaQuery handler,
            CancellationToken cancellationToken)
        {
            var media = await handler.Handle(
                new CommentMediaRequest(id, mediaItemId),
                cancellationToken);
            if (media is null)
                return Results.NotFound();

            httpContext.Response.Headers.CacheControl = "private, no-store";
            httpContext.Response.Headers.Vary = "Authorization, Cookie";
            return Results.Stream(
                media.Content,
                media.MimeType,
                enableRangeProcessing: true);
        }

        private static IResult InvalidCriteria(string title, string detail) =>
            Results.Problem(
                title: title,
                detail: detail,
                statusCode: StatusCodes.Status400BadRequest);

        private static string? ValidateCursorRequest(
            CommentsCriteria criteria,
            string? cursor)
        {
            if (criteria.PageSize is > 50)
                return "PageSize cannot exceed 50 for cursor pagination.";
            if (criteria.Targets is { Length: > 20 })
                return "Cursor pagination accepts at most 20 target filters.";
            if (!string.IsNullOrWhiteSpace(cursor) &&
                cursor.Length > CommentCursorCodec.MaximumEncodedLength)
                return "The comment cursor is invalid.";
            return null;
        }
    }
}
